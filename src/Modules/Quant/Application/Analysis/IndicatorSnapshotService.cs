using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Quant.Domain;
using Astryx.Modules.Quant.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>
/// M7 (SYSTEM_DESIGN.md §11.4): the indicator snapshot for a symbol, served from <c>market.indicator_snapshots</c>
/// when the last bar and parameters match, otherwise computed and cached. Data quality is re-derived on every read,
/// because staleness depends on the time, not on the bars.
/// </summary>
public sealed class IndicatorSnapshotService(
    QuantDbContext db, IPriceHistoryProvider history, StockAnalysisEngine engine, RelativeStrengthRanks ranks, TimeProvider timeProvider)
{
    public static readonly Error InstrumentNotFound = new("instrument_not_found", "No instrument with that symbol.");
    public static readonly Error InsufficientHistory = new("insufficient_history", $"At least {StockAnalysisEngine.MinBars} daily bars are needed for indicators.");
    public static readonly Error UnsupportedInterval = new("bad_request", "Only interval=1d is supported.");

    private const string Timeframe = "1d";

    public async Task<Result<IndicatorSnapshot>> GetAsync(string symbol, string? interval, CancellationToken cancellationToken)
    {
        if (interval is not (null or Timeframe))
        {
            return UnsupportedInterval;
        }

        if (await history.GetDailyAsync(symbol, StockAnalysisEngine.LookbackBars, cancellationToken) is not { } daily)
        {
            return InstrumentNotFound;
        }

        if (daily.Bars.Count < StockAnalysisEngine.MinBars || daily.LastBarAt is not { } asOfBar)
        {
            return InsufficientHistory;
        }

        var hash = StockAnalysisEngine.ParamsHash;
        var cached = await db.IndicatorSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.InstrumentId == daily.InstrumentId && s.Timeframe == Timeframe && s.ParamsHash == hash && s.AsOfBar == asOfBar, cancellationToken);
        if (cached is not null)
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(cached.Values)!;
            var values = StockAnalysisEngine.ValueNames.ToDictionary(name => name, name => stored.GetValueOrDefault(name));
            var insufficient = values.Where(v => v.Value is null).Select(v => v.Key).ToList();
            return new IndicatorSnapshot(daily.Symbol, Timeframe, daily.Bars[^1].SessionDate, daily.Bars.Count, StockAnalysisEngine.Quality(daily, insufficient), values, insufficient);
        }

        var snapshot = engine.Analyze(daily, await ranks.RankAsync(daily.Symbol, cancellationToken));
        await StoreAsync(daily.InstrumentId, asOfBar, snapshot, cancellationToken);
        return snapshot;
    }

    private async Task StoreAsync(Guid instrumentId, DateTimeOffset asOfBar, IndicatorSnapshot snapshot, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO market.indicator_snapshots (id, instrument_id, timeframe, as_of_bar, params_hash, values, data_quality, computed_at)
            VALUES ({Ids.New(timeProvider)}, {instrumentId}, {Timeframe}, {asOfBar}, {StockAnalysisEngine.ParamsHash}, {JsonSerializer.Serialize(snapshot.Values)}::jsonb, {snapshot.DataQuality}, {now})
            ON CONFLICT (instrument_id, timeframe, params_hash, as_of_bar) DO NOTHING
            """,
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM market.indicator_snapshots
            WHERE instrument_id = {instrumentId} AND timeframe = {Timeframe} AND id NOT IN (
                SELECT id FROM market.indicator_snapshots WHERE instrument_id = {instrumentId} AND timeframe = {Timeframe}
                ORDER BY as_of_bar DESC, computed_at DESC LIMIT {IndicatorSnapshotRecord.Keep})
            """,
            cancellationToken);
    }
}
