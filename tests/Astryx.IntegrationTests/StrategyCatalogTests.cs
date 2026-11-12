using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Astryx.Modules.Strategies.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Astryx.IntegrationTests;

/// <summary>The seeded strategy catalog, ST1 and ST3–ST5, and version immutability (SYSTEM_DESIGN.md §7.8, §11.9, T-063).</summary>
public class StrategyCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync() => _client = await TestUsers.SignedInClientAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_the_five_design_strategies_with_their_backtest_headline()
    {
        var items = (await _client.GetFromJsonAsync<JsonElement>("/api/v1/strategies")).GetProperty("items").EnumerateArray().ToList();

        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["Base Breakout v3", "Trend Momentum", "Trend Pullback", "RSI(2) Mean Reversion", "Earnings Drift"]);
        var breakout = items[0];
        breakout.GetProperty("status").GetString().ShouldBe("active");
        breakout.GetProperty("version").GetInt32().ShouldBe(3);
        breakout.GetProperty("liveSince").GetString().ShouldBe("2025-03-03");
        var metrics = breakout.GetProperty("metrics");
        (metrics.GetProperty("cagr").GetString(), metrics.GetProperty("sharpe").GetString(), metrics.GetProperty("maxDrawdown").GetString(), metrics.GetProperty("winRate").GetString())
            .ShouldBe(("0.169", "1.31", "-0.148", "0.46")); // design: Strategy lab
        var live = breakout.GetProperty("live");
        live.GetProperty("mtdPct").GetString().ShouldBe("0.028");
        live.GetProperty("sinceInceptionPct").GetString().ShouldBe("0.114");
        live.GetProperty("illustrative").GetBoolean().ShouldBeTrue();
        var sparkline = breakout.GetProperty("sparkline").EnumerateArray().Select(p => decimal.Parse(p.GetString()!)).ToList();
        sparkline.Count.ShouldBe(60);
        (sparkline[^1] / sparkline[0]).ShouldBe(6.02m, 0.005m); // the design's growth of $1
        items[2].GetProperty("status").GetString().ShouldBe("active_guarded"); // Trend Pullback "Active · guard"
    }

    [Theory]
    [InlineData("base_breakout")]
    [InlineData("id")]
    public async Task Detail_has_rules_the_published_parameters_and_the_latest_run(string reference)
    {
        var path = reference == "id" ? CatalogSeed.StrategyId("base_breakout").ToString() : reference;
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/strategies/{path}");

        detail.GetProperty("summary").GetProperty("code").GetString().ShouldBe("base_breakout");
        detail.GetProperty("rules").GetArrayLength().ShouldBeGreaterThan(2);
        var version = detail.GetProperty("version");
        version.GetProperty("number").GetInt32().ShouldBe(3);
        version.GetProperty("state").GetString().ShouldBe("published");
        var parameters = version.GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!);
        parameters.Keys.ShouldBe(["baseWeeks", "volumeMultiple", "minRelStrength", "stopAtr"]);
        parameters["baseWeeks"].GetProperty("value").GetInt32().ShouldBe(6); // design 12: "6 wk · range 4–10"
        parameters["baseWeeks"].GetProperty("min").GetInt32().ShouldBe(4);
        parameters["baseWeeks"].GetProperty("max").GetInt32().ShouldBe(10);
        parameters["volumeMultiple"].GetProperty("default").GetString().ShouldBe("1.8");
        parameters["stopAtr"].GetProperty("value").GetString().ShouldBe("1.50");
        var full = detail.GetProperty("latestRun").GetProperty("metrics").GetProperty("full");
        full.GetProperty("trades").GetInt32().ShouldBe(412);
        full.GetProperty("profitFactor").GetString().ShouldBe("1.86");
        full.GetProperty("expectancyR").GetString().ShouldBe("0.42");
        detail.GetProperty("latestRun").GetProperty("metrics").GetProperty("outOfSample").GetProperty("cagr").GetString().ShouldBe("0.141");
        detail.GetProperty("todaysSignals").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_latest_run_has_monthly_curves_from_Jan_2015_to_Jun_2026()
    {
        var run = await _client.GetFromJsonAsync<JsonElement>("/api/v1/strategies/base_breakout/runs/latest");

        run.GetProperty("runNumber").GetInt32().ShouldBe(1);
        run.GetProperty("version").GetInt32().ShouldBe(3);
        run.GetProperty("oosStart").GetString().ShouldBe("2023-01-01");
        var equity = run.GetProperty("equityCurve").EnumerateArray().ToList();
        equity.Count.ShouldBe(138);
        equity[0][0].GetString().ShouldBe("2015-01-01");
        equity[0][1].GetString().ShouldBe("100000.00");
        run.GetProperty("drawdownCurve").EnumerateArray().Min(p => decimal.Parse(p[1].GetString()!)).ShouldBe(-0.148m, 0.0005m);
        run.GetProperty("assumptions").GetProperty("survivorship").GetString()!.ShouldContain("biased upward");

        var byId = await _client.GetAsync($"/api/v1/strategies/base_breakout/runs/{run.GetProperty("id").GetString()}");
        byId.StatusCode.ShouldBe(HttpStatusCode.OK);
        var otherStrategysRun = CatalogSeed.RunId("trend_momentum", 1);
        (await _client.GetAsync($"/api/v1/strategies/base_breakout/runs/{otherStrategysRun}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Month_to_date_summary_is_illustrative_until_portfolio_attribution()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/strategies/summary?period=mtd");

        body.GetProperty("period").GetString().ShouldBe("mtd");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(5);
        items[0].GetProperty("pnlPct").GetString().ShouldBe("0.028");
        items.ShouldAllBe(i => i.GetProperty("illustrative").GetBoolean());
        items.Single(i => i.GetProperty("name").GetString() == "Trend Pullback").GetProperty("pnl").GetString().ShouldBe("-712.40");
        (await _client.GetAsync("/api/v1/strategies/summary?period=ytd")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_strategies_are_404_and_a_session_is_required()
    {
        (await _client.GetAsync("/api/v1/strategies/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/v1/strategies/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetAsync("/api/v1/strategies")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Published_and_archived_versions_are_immutable_but_drafts_are_not()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        var published = CatalogSeed.VersionId("base_breakout", 3);
        var archived = CatalogSeed.VersionId("base_breakout", 1);

        var changePublished = () => db.Database.ExecuteSqlAsync($"UPDATE strategy.strategy_versions SET params = '{{\"baseWeeks\": 9}}'::jsonb WHERE id = {published}");
        (await Should.ThrowAsync<PostgresException>(changePublished)).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        var changeArchived = () => db.Database.ExecuteSqlAsync($"UPDATE strategy.strategy_versions SET params = '{{}}'::jsonb WHERE id = {archived}");
        (await Should.ThrowAsync<PostgresException>(changeArchived)).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        var backToDraft = () => db.Database.ExecuteSqlAsync($"UPDATE strategy.strategy_versions SET state = 'draft' WHERE id = {published}");
        await Should.ThrowAsync<PostgresException>(backToDraft);

        var draft = Guid.NewGuid();
        var strategy = CatalogSeed.StrategyId("base_breakout");
        await db.Database.ExecuteSqlAsync($$"""
            INSERT INTO strategy.strategy_versions (id, strategy_id, version_number, state, params, code_version, created_at)
            VALUES ({{draft}}, {{strategy}}, 99, 'draft', '{"baseWeeks": 6}'::jsonb, 'base_breakout@1', now())
            """);
        (await db.Database.ExecuteSqlAsync($"UPDATE strategy.strategy_versions SET params = '{{\"baseWeeks\": 7}}'::jsonb WHERE id = {draft}")).ShouldBe(1);
        (await db.Database.ExecuteSqlAsync($"UPDATE strategy.strategy_versions SET state = 'archived' WHERE id = {published} AND false")).ShouldBe(0);
        await db.Database.ExecuteSqlAsync($"DELETE FROM strategy.strategy_versions WHERE id = {draft}");
    }
}
