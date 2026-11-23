using Astryx.Contracts.Events.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record EodBarSyncJob : IJob
{
    public static string JobType => "market.eod_bar_sync.v1";
}

/// <summary>
/// After each close (16:30 ET; an empty database backfills at once): final daily bars and corporate actions for every
/// active instrument, upserted by primary key so a retried or resumed run is harmless (SYSTEM_DESIGN.md §16.3, §19.2).
/// Instruments are processed in order of their last stored bar, so a run cut short by the rate budget resumes where it
/// stopped.
/// </summary>
public sealed class EodBarSyncHandler(MarketDataDbContext db, IMarketDataProvider provider, SyncStateStore state, SyncRunner runner, MarketClock clock, IOptions<IngestionOptions> options)
    : IJobHandler<EodBarSyncJob>
{
    public async Task HandleAsync(EodBarSyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (!await db.Instruments.AnyAsync(i => i.IsActive, cancellationToken))
        {
            return; // before the first instrument sync; recording success now would skip the backfill until the next close
        }

        var target = await LatestClosedSessionAsync(cancellationToken);
        var last = await state.GetAsync(provider.Code, SyncDataset.EodBars, cancellationToken);
        if (target is null || (last?.LastErrorCode is null && last?.LastSuccessAt >= target.CloseAt!.Value.AddMinutes(30)))
        {
            return;
        }

        await runner.RunAsync(SyncDataset.EodBars, meter => SyncAsync(target.SessionDate, meter, cancellationToken), rethrowUnavailable: true, cancellationToken);
    }

    /// <summary>
    /// One-time deepening of daily history to <paramref name="days"/> calendar days back for every active instrument
    /// (T-160: backtests need more than the default backfill, and the relative-strength rank alone needs a year of
    /// warm-up). Fetches only what is older than each instrument's earliest stored bar; the provider's own rate limiter
    /// paces the calls. Returns the number of bars stored.
    /// </summary>
    public async Task<int> DeepenAsync(int days, CancellationToken cancellationToken)
    {
        var target = clock.Today.AddDays(-days);
        var instruments = await db.Instruments
            .Where(i => i.IsActive)
            .Select(i => new { i.Id, i.Symbol, Earliest = db.PriceBars.Where(b => b.InstrumentId == i.Id && b.Timeframe == Timeframe.OneDay).Min(b => (DateOnly?)b.SessionDate) })
            .ToListAsync(cancellationToken);

        var stored = 0;
        foreach (var instrument in instruments.Where(i => i.Earliest is { } earliest && earliest > target.AddDays(7)).OrderBy(i => i.Symbol))
        {
            var bars = (await provider.GetBarsAsync(instrument.Symbol, BarInterval.OneDay, target, instrument.Earliest!.Value.AddDays(-1), cancellationToken))
                .Where(b => b.IsFinal && IsValid(b)).ToList();
            await UpsertDailyBarsAsync(instrument.Id, bars, cancellationToken);
            stored += bars.Count;
        }

        return stored;
    }

    /// <summary>The newest session that closed at least 30 minutes ago, so its final bar is published.</summary>
    private async Task<ProviderSession?> LatestClosedSessionAsync(CancellationToken cancellationToken)
    {
        for (var date = clock.Today; date >= clock.Today.AddDays(-10); date = date.AddDays(-1))
        {
            if (await clock.SessionAsync(date, cancellationToken) is { IsHoliday: false } session && session.CloseAt!.Value.AddMinutes(30) <= clock.Now)
            {
                return session;
            }
        }

        return null;
    }

    private async Task SyncAsync(DateOnly through, RequestMeter meter, CancellationToken cancellationToken)
    {
        var pending = await db.Instruments
            .Where(i => i.IsActive)
            .Select(i => new
            {
                i.Id,
                i.Symbol,
                LastBar = db.PriceBars.Where(b => b.InstrumentId == i.Id && b.Timeframe == Timeframe.OneDay && b.IsFinal).Max(b => (DateOnly?)b.SessionDate),
                LastAction = db.CorporateActions.Where(a => a.InstrumentId == i.Id).Max(a => (DateOnly?)a.ExDate),
            })
            .ToListAsync(cancellationToken);

        foreach (var instrument in pending.Where(p => p.LastBar is null || p.LastBar < through).OrderBy(p => p.LastBar ?? DateOnly.MinValue).ThenBy(p => p.Symbol))
        {
            var from = instrument.LastBar?.AddDays(1) ?? through.AddDays(-options.Value.BackfillDays);
            meter.Take();
            var bars = await provider.GetBarsAsync(instrument.Symbol, BarInterval.OneDay, from, through, cancellationToken);
            await UpsertDailyBarsAsync(instrument.Id, bars.Where(b => b.IsFinal && IsValid(b)).ToList(), cancellationToken);

            if (provider.Capabilities.CorporateActions)
            {
                meter.Take();
                var actions = await provider.GetCorporateActionsAsync(instrument.Symbol, instrument.LastAction?.AddDays(1) ?? from.AddYears(-3), cancellationToken);
                await UpsertCorporateActionsAsync(instrument.Id, instrument.Symbol, actions, cancellationToken);
            }
        }
    }

    private static bool IsValid(ProviderBar bar) =>
        bar.Low > 0 && bar.Low <= Math.Min(bar.Open, bar.Close) && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Volume >= 0;

    private async Task UpsertDailyBarsAsync(Guid instrumentId, IReadOnlyList<ProviderBar> bars, CancellationToken cancellationToken)
    {
        if (bars.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO market.price_bars (instrument_id, timeframe, bar_start, session_date, open, high, low, close, volume, is_final, source, ingested_at)
            SELECT @instrument, @timeframe, b.bar_start, b.session_date, b.open, b.high, b.low, b.close, b.volume, true, @source, @now
            FROM unnest(@starts, @dates, @opens, @highs, @lows, @closes, @volumes) AS b(bar_start, session_date, open, high, low, close, volume)
            ON CONFLICT (instrument_id, timeframe, bar_start) DO UPDATE SET
                session_date = excluded.session_date, open = excluded.open, high = excluded.high, low = excluded.low,
                close = excluded.close, volume = excluded.volume, is_final = true, source = excluded.source, ingested_at = excluded.ingested_at
            """,
            [
                new NpgsqlParameter("instrument", instrumentId),
                new NpgsqlParameter("timeframe", Timeframe.OneDay),
                new NpgsqlParameter("source", provider.Code),
                new NpgsqlParameter("now", clock.Now),
                new NpgsqlParameter("starts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = bars.Select(b => b.BarStart).ToArray() },
                new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = bars.Select(b => b.SessionDate).ToArray() },
                new NpgsqlParameter("opens", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Open).ToArray() },
                new NpgsqlParameter("highs", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.High).ToArray() },
                new NpgsqlParameter("lows", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Low).ToArray() },
                new NpgsqlParameter("closes", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Close).ToArray() },
                new NpgsqlParameter("volumes", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = bars.Select(b => b.Volume).ToArray() },
            ],
            cancellationToken);
    }

    /// <summary>Upserts the actions; a new or changed one publishes <see cref="CorporateActionRecorded"/> so cached indicators are dropped.</summary>
    private async Task UpsertCorporateActionsAsync(Guid instrumentId, string symbol, IReadOnlyList<ProviderCorporateAction> actions, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var action in actions)
        {
            var affected = await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO market.corporate_actions (id, instrument_id, action_type, ex_date, ratio, cash_amount, source)
                VALUES ({Guid.CreateVersion7(clock.Now)}, {instrumentId}, {action.ActionType}, {action.ExDate}, {action.Ratio}, {action.CashAmount}, {provider.Code})
                ON CONFLICT (instrument_id, action_type, ex_date) DO UPDATE SET ratio = excluded.ratio, cash_amount = excluded.cash_amount, source = excluded.source
                WHERE (market.corporate_actions.ratio, market.corporate_actions.cash_amount) IS DISTINCT FROM (excluded.ratio, excluded.cash_amount)
                """,
                cancellationToken);
            if (affected > 0)
            {
                db.Publish(new CorporateActionRecorded(instrumentId, symbol, action.ActionType, action.ExDate));
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
