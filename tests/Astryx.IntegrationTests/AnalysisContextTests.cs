using System.Text.Json.Nodes;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Discovery.Application.Scanner;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>ctx.v1 from the real MarketData, Quant and Discovery modules (SYSTEM_DESIGN.md §12.4, T-071).</summary>
public class AnalysisContextTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        await factory.RefreshQuotesAsync(); // the context builder refuses stale quotes
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        var client = await TestUsers.SignedInClientAsync(factory);
        (await client.GetAsync("/api/v1/instruments/NVDA/news")).EnsureSuccessStatusCode(); // ingests the fixture news
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NVDA_gets_the_design_context_and_renders_with_the_active_prompt()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var builder = scope.ServiceProvider.GetRequiredService<AnalysisContextBuilder>();

        var result = await builder.BuildAsync("nvda", DateTimeOffset.UtcNow, default);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        var json = JsonNode.Parse(result.Value.ToJson())!;
        json["symbol"]!.GetValue<string>().ShouldBe("NVDA");
        json["dataQuality"]!["status"]!.GetValue<string>().ShouldBeOneOf("ok", "gaps");
        json["recentBars"]!.AsArray().Count.ShouldBe(AnalysisContextBuilder.RecentBars);
        json["quote"]!["last"]!.GetValue<string>().ShouldBe("186.40");
        json["indicators"]!["atr14"]!.GetValue<string>().ShouldBe("5.53");
        json["indicators"]!["relVolume20"]!.GetValue<string>().ShouldBe("2.40");
        json["signal"]!["levels"]!.ToJsonString().ShouldBe("""{"entry":"186.50","stop":"178.20","target":"204.00"}""");
        json["constraints"]!["priceBand"]!.ToJsonString().ShouldBe("""{"min":"149.12","max":"223.68"}""");
        var news = json["news"]!.AsArray();
        news.Count.ShouldBeInRange(1, AnalysisContextBuilder.MaxNews);
        news.ShouldAllBe(n => n!["headline"]!.GetValue<string>().Length <= NewsSanitizer.HeadlineMax && !n["headline"]!.GetValue<string>().Contains("http", StringComparison.Ordinal));

        var messages = scope.ServiceProvider.GetRequiredService<PromptRegistry>().GetActive(PromptRegistry.StockAnalysis).Render(result.Value);
        messages[1].Content.ShouldContain("\"atr14\":\"5.53\"");
    }

    [Fact]
    public async Task Unknown_symbols_fail_before_any_model_call()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<AnalysisContextBuilder>().BuildAsync("ZZZZZ", DateTimeOffset.UtcNow, default);

        result.Error!.Code.ShouldBe("instrument_not_found");
    }
}
