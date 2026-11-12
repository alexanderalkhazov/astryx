using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Scanner status and signal endpoints D1–D4 (SYSTEM_DESIGN.md §11.4, T-066).</summary>
public class SignalEndpointTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            if (!await scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>().ScannerRuns.AnyAsync())
            {
                await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), new JobContext(Guid.NewGuid(), 1, 1, null), default);
            }
        }

        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> GetAsync(string path) => await _client.GetFromJsonAsync<JsonElement>(path);

    [Fact]
    public async Task Status_is_live_after_a_clean_run()
    {
        var status = await GetAsync("/api/v1/scanner/status");

        status.GetProperty("status").GetString().ShouldBeOneOf("live", "degraded");
        status.GetProperty("symbolsTotal").GetInt32().ShouldBe(1031);
        status.GetProperty("lastRunAt").GetString()!.ShouldEndWith("Z");
        status.GetProperty("errorCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Lists_NVDA_as_the_top_breakout_with_chip_counts()
    {
        var page = await GetAsync("/api/v1/signals");

        var nvda = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("symbol").GetString() == "NVDA");
        nvda.GetProperty("rank").GetInt32().ShouldBe(1);
        nvda.GetProperty("type").GetString().ShouldBe("breakout");
        nvda.GetProperty("score").GetString().ShouldBe("87");
        nvda.GetProperty("name").GetString().ShouldBe("NVIDIA Corp.");
        nvda.GetProperty("sector").GetString().ShouldBe("Semiconductors");
        nvda.GetProperty("price").GetString().ShouldBe("186.40");
        nvda.GetProperty("changePct").GetString().ShouldBe("0.0342");
        var levels = nvda.GetProperty("levels");
        (levels.GetProperty("orderType").GetString(), levels.GetProperty("entry").GetString(), levels.GetProperty("stop").GetString(), levels.GetProperty("target").GetString())
            .ShouldBe(("buy_stop", "186.50", "178.20", "204.00"));
        nvda.GetProperty("metrics").GetProperty("rewardRisk").GetString().ShouldBe("2.11");
        var strategy = nvda.GetProperty("strategy");
        (strategy.GetProperty("name").GetString(), strategy.GetProperty("version").GetInt32()).ShouldBe(("Base Breakout v3", 3));
        nvda.GetProperty("scoreBreakdown").ValueKind.ShouldBe(JsonValueKind.Null); // D4 only
        var sparkline = nvda.GetProperty("sparkline").EnumerateArray().Select(p => p.GetString()).ToList();
        sparkline.Count.ShouldBe(20);
        sparkline[^1].ShouldBe("186.40");
        nvda.GetProperty("volume").GetInt64().ShouldBe(412_800_000);
        var counts = page.GetProperty("counts");
        counts.GetProperty("breakout").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        counts.GetProperty("all").GetInt32().ShouldBe(page.GetProperty("items").GetArrayLength());
        page.GetProperty("scanner").GetProperty("lastRunAt").GetString().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("type=momentum", 0)]
    [InlineData("type=breakout&symbol=nvda", 1)]
    [InlineData("minScore=88", 0)]
    [InlineData("minScore=87&marketCap=mega&sector=semiconductors&horizon=swing", 1)]
    [InlineData("marketCap=large&symbol=NVDA", 0)] // $4.55T is mega
    [InlineData("marketCap=small&symbol=NVDA", 0)]
    public async Task Filters_narrow_the_list_and_the_count(string query, int expected)
    {
        var page = await GetAsync($"/api/v1/signals?{query}");
        var count = await GetAsync($"/api/v1/signals/count?{query}");

        page.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("symbol").GetString() == "NVDA").ShouldBe(expected);
        if (query.Contains("symbol", StringComparison.Ordinal))
        {
            count.GetProperty("count").GetInt32().ShouldBe(expected);
        }
    }

    [Fact]
    public async Task Counts_ignore_the_type_filter_so_every_chip_keeps_its_number()
    {
        var all = (await GetAsync("/api/v1/signals")).GetProperty("counts");
        var momentum = (await GetAsync("/api/v1/signals?type=momentum")).GetProperty("counts");

        momentum.ToString().ShouldBe(all.ToString());
    }

    [Theory]
    [InlineData("type=nope")]
    [InlineData("sort=price")]
    [InlineData("limit=51")]
    [InlineData("minScore=101")]
    [InlineData("cursor=@@@")]
    [InlineData("marketCap=huge")]
    public async Task Invalid_filters_are_400(string query)
    {
        (await _client.GetAsync($"/api/v1/signals?{query}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pages_follow_the_cursor_without_gaps_or_repeats()
    {
        var added = await AddSignalsAsync(["AMD", "CRM", "COST", "JPM", "AAPL"], baseScore: 60);
        try
        {
            var seen = new List<string>();
            string? cursor = null;
            do
            {
                var page = await GetAsync($"/api/v1/signals?limit=2&sort=score{(cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")}");
                seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("symbol").GetString()!));
                cursor = page.GetProperty("nextCursor").GetString();
            }
            while (cursor is not null);

            seen.Distinct().Count().ShouldBe(seen.Count);
            seen[0].ShouldBe("NVDA"); // highest score
            seen.ShouldContain("AAPL");
            var byRr = (await GetAsync("/api/v1/signals?sort=rr&limit=50")).GetProperty("items").EnumerateArray().Select(i => decimal.Parse(i.GetProperty("metrics").GetProperty("rewardRisk").GetString()!)).ToList();
            byRr.ShouldBe(byRr.OrderByDescending(x => x).ToList());
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>().Signals.Where(s => added.Contains(s.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Detail_has_the_score_breakdown()
    {
        var id = (await GetAsync("/api/v1/signals?symbol=NVDA")).GetProperty("items")[0].GetProperty("id").GetString();

        var detail = await GetAsync($"/api/v1/signals/{id}");

        detail.GetProperty("reason").GetString().ShouldBe("Cleared a 6-week base at $185.90 on 2.4× average volume.");
        var breakdown = detail.GetProperty("scoreBreakdown").EnumerateArray().ToList();
        breakdown.Count.ShouldBe(5);
        breakdown.Sum(b => decimal.Parse(b.GetProperty("contribution").GetString()!)).ShouldBe(87m, 0.5m);
        detail.GetProperty("proposalId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await _client.GetAsync($"/api/v1/signals/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_scanner_that_fell_behind_moving_data_is_unavailable_with_SCN_504()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>();
        var run = await db.ScannerRuns.OrderByDescending(r => r.StartedAt).FirstAsync();
        var (finished, quotes) = (run.FinishedAt, run.QuotesAsOf);
        try
        {
            // Data moved on (quotes newer than the run saw) and the run is an hour old: more than two 15-minute intervals.
            run.FinishedAt = DateTimeOffset.UtcNow.AddHours(-1);
            run.QuotesAsOf = quotes?.AddMinutes(-5);
            await db.SaveChangesAsync();

            var status = await GetAsync("/api/v1/scanner/status");
            status.GetProperty("status").GetString().ShouldBe("unavailable");
            status.GetProperty("errorCode").GetString().ShouldBe("SCN-504");
            (await _client.GetAsync("/api/v1/signals")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

            // An old run with no newer data (overnight) stays current.
            run.QuotesAsOf = quotes;
            await db.SaveChangesAsync();
            (await GetAsync("/api/v1/scanner/status")).GetProperty("status").GetString().ShouldBeOneOf("live", "degraded");
        }
        finally
        {
            run.FinishedAt = finished;
            run.QuotesAsOf = quotes;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Extra active signals on other instruments in the latest run, for paging.</summary>
    private async Task<List<Guid>> AddSignalsAsync(IReadOnlyList<string> symbols, int baseScore)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>();
        var market = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var template = await db.Signals.AsNoTracking().FirstAsync(s => s.Symbol == "NVDA" && s.Status == SignalStatus.Active);
        var ids = new List<Guid>();
        for (var i = 0; i < symbols.Count; i++)
        {
            var instrument = await market.Instruments.SingleAsync(x => x.Symbol == symbols[i]);
            var id = Guid.NewGuid();
            ids.Add(id);
            db.Signals.Add(new Signal
            {
                Id = id, ScannerRunId = template.ScannerRunId, InstrumentId = instrument.Id, Symbol = instrument.Symbol, StrategyVersionId = Guid.NewGuid(), StrategyId = template.StrategyId,
                StrategyName = template.StrategyName, StrategyVersion = 3, SignalType = "breakout", Horizon = "swing", Score = baseScore + i, ScoreBreakdown = "[]", Reason = "test",
                OrderType = "buy_stop", EntryPrice = 100m, StopPrice = 95m, TargetPrice = 110m + i, Metrics = $$"""{"rewardRisk":"{{(2m + i * 0.1m).ToString(System.Globalization.CultureInfo.InvariantCulture)}}"}""",
                Status = SignalStatus.Active, EvaluatedAt = template.EvaluatedAt, DataAsOf = template.DataAsOf, ExpiresAt = template.ExpiresAt,
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }
}

/// <summary>Before any scan the scanner is unavailable and signals are hidden (SYSTEM_DESIGN.md §11.4).</summary>
public class SignalUnavailableTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Signals_are_503_and_status_unavailable_without_a_run()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/scanner/status");
        status.GetProperty("status").GetString().ShouldBe("unavailable");
        status.GetProperty("errorCode").GetString().ShouldBe("SCN-404");

        var list = await client.GetAsync("/api/v1/signals");
        list.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("scanner_unavailable");
        (await client.GetAsync("/api/v1/signals/count")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
