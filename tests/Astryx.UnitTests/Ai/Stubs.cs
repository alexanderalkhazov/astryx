using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Microsoft.Extensions.Options;

namespace Astryx.UnitTests.Ai;

/// <summary>The design's NVDA inputs to the context builder, as stub implementations of the module contracts.</summary>
internal sealed class Stubs : IInstrumentDirectory, IPriceHistoryProvider, IIndicatorSnapshots, IStockResearchSource, IActiveSignalSource
{
    public static readonly DateTimeOffset RequestedAt = new(2026, 11, 9, 19, 32, 10, TimeSpan.Zero);
    public static readonly DateTimeOffset QuoteAt = new(2026, 11, 9, 19, 32, 0, TimeSpan.Zero);

    public static PromptRegistry Registry(string version = "2026-11-09.1")
    {
        var options = new PromptOptions();
        options.Active[PromptRegistry.StockAnalysis] = version;
        return new PromptRegistry(Options.Create(options));
    }

    public AnalysisContextBuilder Builder() => new(this, this, this, this, this);

    public async Task<AnalysisContext> BuildAsync()
    {
        var result = await Builder().BuildAsync("NVDA", RequestedAt, default);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    public ResearchQuote? Quote { get; set; } = new(186.40m, 180.24m, 412883000, QuoteAt, "delayed", Stale: false);

    public string Quality { get; set; } = "ok";

    public List<DateOnly> Missing { get; } = [];

    public List<ResearchNewsItem> News { get; } = [];

    public ActiveSignal? Signal { get; set; } = new("breakout", 87m, "Cleared a 6-week base at $185.90 on 2.4× average volume.", "buy_stop", 186.5m, 178.2m, 204m, QuoteAt);

    public Task<InstrumentSummary?> FindAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult(symbol == "NVDA" ? new InstrumentSummary(Guid.Empty, "NVDA", "NVIDIA Corp.", "Semiconductors", 186.40m, 0.0342m, QuoteAt) : null);

    public Task<IReadOnlyList<InstrumentSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<InstrumentSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<InstrumentSummary>> ListActiveEquitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DailyHistory?> GetDailyAsync(string symbol, int maxBars, CancellationToken cancellationToken, AdjustmentMode mode = AdjustmentMode.SplitOnly)
    {
        var bars = Enumerable.Range(0, 30).Select(i => new DailyBar(new DateOnly(2026, 9, 28).AddDays(i), 170m + i, 172m + i, 169m + i, 171.456m + i, 200_000_000 + i, i < 29)).TakeLast(maxBars).ToList();
        return Task.FromResult<DailyHistory?>(new DailyHistory(Guid.Empty, symbol, bars, QuoteAt, false, []));
    }

    public Task<CloseLadder> GetCloseLadderAsync(IReadOnlyList<int> sessionsBack, CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<IndicatorReadout?> IIndicatorSnapshots.GetDailyAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<IndicatorReadout?>(new IndicatorReadout(symbol, new DateOnly(2026, 11, 9), 300, Quality, Missing, new Dictionary<string, string?>
        {
            ["sma20"] = "176.8412",
            ["sma50"] = "171.2210",
            ["ema20"] = "177.0000",
            ["rsi14"] = "64.2",
            ["macdHist"] = "0.8093",
            ["atr14"] = "5.5337",
            ["relVolume20"] = "2.40",
            ["trend"] = "uptrend",
            ["volatility20Ann"] = "0.4118",
            ["relStrength"] = "94",
        }));

    public Task<ResearchQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(Quote);

    public Task<ResearchFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<ResearchFundamentals?>(new ResearchFundamentals(new DateOnly(2026, 11, 19), 48.23m, 1.714m));

    public Task<IReadOnlyList<ResearchNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, DateTimeOffset until, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ResearchNewsItem>>(News.Where(n => n.PublishedAt >= since && n.PublishedAt <= until).OrderByDescending(n => n.PublishedAt).Take(limit).ToList());

    Task<ActiveSignal?> IActiveSignalSource.GetAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(Signal);
}
