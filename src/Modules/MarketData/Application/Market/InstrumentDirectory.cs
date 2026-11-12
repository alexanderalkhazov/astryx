using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary>
/// Search ranking (SYSTEM_DESIGN.md §11.4 M1): exact ticker, then ticker prefix by market cap, then company names that
/// match at a word start or by trigram similarity. Word-start matching keeps "am" from hitting every "Sample …".
/// </summary>
public sealed class InstrumentDirectory(MarketDataDbContext db) : IInstrumentDirectory
{
    public async Task<IReadOnlyList<InstrumentSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        var like = text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
        var symbolPrefix = like.ToUpperInvariant() + "%";
        var wordStart = like + "%";
        var innerWord = "% " + like + "%";
        var rows = await db.Database
            .SqlQuery<Row>(
                $"""
                SELECT i.id, i.symbol, i.name, i.sector, q.last, q.prev_close, q.as_of, i.market_cap, i.industry
                FROM market.instruments i
                LEFT JOIN market.quotes q ON q.instrument_id = i.id
                WHERE i.is_active
                  AND (i.symbol LIKE {symbolPrefix} OR i.name ILIKE {wordStart} OR i.name ILIKE {innerWord} OR similarity(i.name, {text}) >= 0.4)
                ORDER BY (i.symbol = upper({text})) DESC,
                         (i.symbol LIKE {symbolPrefix}) DESC,
                         CASE WHEN i.symbol LIKE {symbolPrefix} THEN i.market_cap END DESC NULLS LAST,
                         similarity(i.name, {text}) DESC,
                         i.symbol
                LIMIT {limit}
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<InstrumentSummary>> ListActiveEquitiesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<Row>(
                $"""
                SELECT i.id, i.symbol, i.name, i.sector, q.last, q.prev_close, q.as_of, i.market_cap, i.industry
                FROM market.instruments i
                LEFT JOIN market.quotes q ON q.instrument_id = i.id
                WHERE i.is_active AND i.asset_type = 'equity'
                ORDER BY i.symbol
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<InstrumentSummary?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var rows = await db.Database
            .SqlQuery<Row>(
                $"""
                SELECT i.id, i.symbol, i.name, i.sector, q.last, q.prev_close, q.as_of, i.market_cap, i.industry
                FROM market.instruments i LEFT JOIN market.quotes q ON q.instrument_id = i.id
                WHERE i.symbol = {normalized}
                ORDER BY i.is_active DESC
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSummary).SingleOrDefault();
    }

    public async Task<IReadOnlyList<InstrumentSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var array = ids.ToArray();
        var rows = await db.Database
            .SqlQuery<Row>(
                $"""
                SELECT i.id, i.symbol, i.name, i.sector, q.last, q.prev_close, q.as_of, i.market_cap, i.industry
                FROM market.instruments i LEFT JOIN market.quotes q ON q.instrument_id = i.id
                WHERE i.id = ANY({array})
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToList();
    }

    private static InstrumentSummary ToSummary(Row row) => new(
        row.Id,
        row.Symbol,
        row.Name,
        row.Sector,
        row.Last is { } last ? Math.Round(last, Math.Abs(last) >= 1 ? 2 : 4, MidpointRounding.AwayFromZero) : null,
        row.Last is { } l && row.PrevClose is { } prev && prev != 0 ? Math.Round((l / prev) - 1, 4, MidpointRounding.AwayFromZero) : null,
        row.AsOf,
        row.MarketCap,
        row.Industry);

    private sealed record Row(Guid Id, string Symbol, string Name, string? Sector, decimal? Last, decimal? PrevClose, DateTimeOffset? AsOf, decimal? MarketCap, string? Industry);
}
