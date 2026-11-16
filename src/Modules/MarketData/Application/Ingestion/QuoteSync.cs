using Astryx.Contracts.Events.MarketData;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Observability;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record QuoteSyncJob : IJob
{
    public static string JobType => "market.quote_sync.v1";
}

/// <summary>Where the round-robin over the rest of the universe resumes next cycle (per process; best effort).</summary>
public sealed class QuoteRotation
{
    public int Offset { get; set; }
}

/// <summary>
/// Every 60 s in the regular session, plus once after the close and on an empty quote table: snapshot quotes for
/// priority symbols (positions, working orders, watchlists, signals), then the rest of the universe round-robin within
/// the rate budget (SYSTEM_DESIGN.md §16.3, §19.2). Bad ticks are dropped; a changed quote bumps <c>quote_seq</c> and
/// joins one <see cref="QuotesUpdated"/> event published in the same transaction.
/// </summary>
public sealed class QuoteSyncHandler(
    MarketDataDbContext db,
    IMarketDataProvider provider,
    IEnumerable<IPrioritySymbolSource> prioritySources,
    SyncRunner runner,
    MarketClock clock,
    QuoteRotation rotation,
    IOptions<IngestionOptions> options,
    Astryx.Modules.MarketData.Infrastructure.Providers.Fake.MarketDataFaults faults,
    ILogger<QuoteSyncHandler> logger)
    : IJobHandler<QuoteSyncJob>
{
    public async Task HandleAsync(QuoteSyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (!await IsDueAsync(cancellationToken))
        {
            return;
        }

        await runner.RunAsync(SyncDataset.Quotes, meter => SyncAsync(meter, cancellationToken), rethrowUnavailable: false, cancellationToken);
    }

    private async Task<bool> IsDueAsync(CancellationToken cancellationToken)
    {
        var session = await clock.CurrentSessionAsync(cancellationToken);
        if (clock.Now < session.CloseAt || faults.SessionForcedOpen) // the E2E session override keeps quotes live (T-110)
        {
            return true;
        }

        // Closed: refresh once to capture closing values, or to fill quotes for instruments that have none.
        var active = await db.Instruments.CountAsync(i => i.IsActive, cancellationToken);
        var quoted = await db.Quotes.CountAsync(cancellationToken);
        var newest = await db.Quotes.MaxAsync(q => (DateTimeOffset?)q.AsOf, cancellationToken);
        return quoted < active || newest < session.CloseAt;
    }

    private async Task SyncAsync(RequestMeter meter, CancellationToken cancellationToken)
    {
        var instruments = await db.Instruments.Where(i => i.IsActive).OrderBy(i => i.Symbol).Select(i => new { i.Id, i.Symbol }).ToListAsync(cancellationToken);
        if (instruments.Count == 0)
        {
            return;
        }

        var ids = instruments.ToDictionary(i => i.Symbol, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = await OrderSymbolsAsync(instruments.Select(i => i.Symbol).ToList(), cancellationToken);
        var batchSize = Math.Max(1, provider.Capabilities.MaxSymbolsPerRequest);
        var received = new List<ProviderQuote>();
        try
        {
            foreach (var batch in ordered.Chunk(batchSize))
            {
                meter.Take();
                received.AddRange(await provider.GetQuotesAsync(batch, cancellationToken));
            }
        }
        finally
        {
            // Store what arrived even when the budget ran out part-way.
            await StoreAsync(received, ids, cancellationToken);
            if (await db.Quotes.MinAsync(q => (DateTimeOffset?)q.AsOf, CancellationToken.None) is { } oldest)
            {
                AstryxTelemetry.QuoteAge(clock.Now - oldest);
            }
        }
    }

    /// <summary>Priority symbols first (by source priority), then the universe starting at the rotation offset.</summary>
    private async Task<List<string>> OrderSymbolsAsync(List<string> universe, CancellationToken cancellationToken)
    {
        var known = universe.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in prioritySources.OrderBy(s => s.Priority))
        {
            foreach (var symbol in await source.GetSymbolsAsync(cancellationToken))
            {
                if (known.Contains(symbol) && seen.Add(symbol))
                {
                    ordered.Add(symbol);
                }
            }
        }

        var start = rotation.Offset % universe.Count;
        var rest = universe.Skip(start).Concat(universe.Take(start)).Where(s => !seen.Contains(s)).ToList();
        ordered.AddRange(rest);
        rotation.Offset = (start + Math.Max(1, provider.Capabilities.MaxSymbolsPerRequest)) % universe.Count;
        return ordered;
    }

    private bool IsValid(ProviderQuote quote) =>
        quote.Last > 0 && quote.PrevClose > 0 && quote.High >= quote.Low && quote.Volume >= 0
        && Math.Abs((quote.Last / quote.PrevClose) - 1) <= options.Value.MaxQuoteMoveFromPrevClose;

    private async Task StoreAsync(List<ProviderQuote> received, Dictionary<string, Guid> ids, CancellationToken cancellationToken)
    {
        var valid = received.Where(q => ids.ContainsKey(q.Symbol) && IsValid(q)).ToList();
        foreach (var bad in received.Except(valid))
        {
            logger.LogWarning("Dropped bad tick for {Symbol}: last {Last}, previous close {PrevClose}", bad.Symbol, bad.Last, bad.PrevClose);
        }

        if (valid.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.Database
            .SqlQueryRaw<ChangedQuote>(
                """
                INSERT INTO market.quotes AS q (instrument_id, last, bid, ask, open, high, low, prev_close, volume, as_of, received_at, source, quality, quote_seq)
                SELECT u.instrument_id, u.last, u.bid, u.ask, u.open, u.high, u.low, u.prev_close, u.volume, u.as_of, @now, @source, u.quality, 1
                FROM unnest(@ids, @lasts, @bids, @asks, @opens, @highs, @lows, @prevs, @volumes, @asofs, @qualities)
                    AS u(instrument_id, last, bid, ask, open, high, low, prev_close, volume, as_of, quality)
                ON CONFLICT (instrument_id) DO UPDATE SET
                    last = excluded.last, bid = excluded.bid, ask = excluded.ask, open = excluded.open, high = excluded.high,
                    low = excluded.low, prev_close = excluded.prev_close, volume = excluded.volume, as_of = excluded.as_of,
                    received_at = excluded.received_at, source = excluded.source, quality = excluded.quality, quote_seq = q.quote_seq + 1
                WHERE (q.last, q.as_of, q.volume, q.high, q.low) IS DISTINCT FROM (excluded.last, excluded.as_of, excluded.volume, excluded.high, excluded.low)
                RETURNING q.instrument_id, q.last, q.as_of, q.quote_seq
                """,
                new NpgsqlParameter("now", clock.Now),
                new NpgsqlParameter("source", provider.Code),
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = valid.Select(q => ids[q.Symbol]).ToArray() },
                new NpgsqlParameter("lasts", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.Last).ToArray() },
                new NpgsqlParameter("bids", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.Bid).ToArray() },
                new NpgsqlParameter("asks", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.Ask).ToArray() },
                new NpgsqlParameter("opens", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.Open).ToArray() },
                new NpgsqlParameter("highs", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.High).ToArray() },
                new NpgsqlParameter("lows", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.Low).ToArray() },
                new NpgsqlParameter("prevs", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = valid.Select(q => q.PrevClose).ToArray() },
                new NpgsqlParameter("volumes", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = valid.Select(q => q.Volume).ToArray() },
                new NpgsqlParameter("asofs", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = valid.Select(q => q.AsOf).ToArray() },
                new NpgsqlParameter("qualities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = valid.Select(q => q.Quality).ToArray() })
            .ToListAsync(cancellationToken);

        if (changed.Count > 0)
        {
            var symbols = ids.ToDictionary(p => p.Value, p => p.Key);
            db.Publish(new QuotesUpdated(changed.Select(c => new QuoteChange(c.InstrumentId, symbols[c.InstrumentId], c.Last, c.AsOf, c.QuoteSeq)).ToList()));
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record ChangedQuote(Guid InstrumentId, decimal Last, DateTimeOffset AsOf, long QuoteSeq);
}
