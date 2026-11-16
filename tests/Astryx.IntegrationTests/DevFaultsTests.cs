using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>An API running as Development with the prototype controls switched on (SYSTEM_DESIGN.md §11.11).</summary>
public sealed class DevFaultsApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Features:DevFaultInjection"] = "true" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
    }
}

public class DevFaultsTests(DevFaultsApiFactory factory) : IClassFixture<DevFaultsApiFactory>
{
    [Fact]
    public async Task Market_data_faults_can_be_toggled_at_runtime()
    {
        var client = factory.CreateClient();
        var provider = factory.Services.GetRequiredService<IMarketDataProvider>();

        var down = await client.PostAsJsonAsync("/dev/faults", new { marketData = "down" });
        down.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await down.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("marketData").GetString().ShouldBe("down");
        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetQuotesAsync(["NVDA"], default));

        var stale = await client.PostAsJsonAsync("/dev/faults", new { marketData = "stale" });
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("frozenAt").ValueKind.ShouldBe(JsonValueKind.String);

        await client.PostAsJsonAsync("/dev/faults", new { marketData = "ok" });
        (await client.GetFromJsonAsync<JsonElement>("/dev/faults")).GetProperty("marketData").GetString().ShouldBe("ok");
        (await provider.GetQuotesAsync(["NVDA"], default)).Single().Last.ShouldBe(186.40m);
    }

    [Fact]
    public async Task The_session_can_be_forced_open_for_E2E_and_returned_to_the_calendar()
    {
        var client = factory.CreateClient();

        var open = await client.PostAsJsonAsync("/dev/faults", new { session = "open" });

        (await open.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetString().ShouldBe("open");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var clock = scope.ServiceProvider.GetRequiredService<Astryx.Contracts.MarketData.IMarketSessionClock>();
            var session = (await clock.TodayAsync(default)).ShouldNotBeNull();
            (clock.Now > session.OpenAt && clock.Now < session.CloseAt).ShouldBeTrue();
        }

        (await (await client.PostAsJsonAsync("/dev/faults", new { session = "real" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetString().ShouldBe("real");
        (await client.PostAsJsonAsync("/dev/faults", new { session = "always" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_fixed_price_reaches_the_fake_provider_until_cleared()
    {
        var client = factory.CreateClient();
        var provider = factory.Services.GetRequiredService<IMarketDataProvider>();

        (await client.PostAsJsonAsync("/dev/faults", new { prices = new Dictionary<string, string> { ["NVDA"] = "186.60" } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await provider.GetQuotesAsync(["NVDA"], default)).Single().Last.ShouldBe(186.60m);

        await client.PostAsJsonAsync("/dev/faults", new { prices = new Dictionary<string, string>() });
        (await provider.GetQuotesAsync(["NVDA"], default)).Single().Last.ShouldBe(186.40m);
        (await client.PostAsJsonAsync("/dev/faults", new { prices = new Dictionary<string, string> { ["NVDA"] = "0" } })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Unknown_modes_are_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/dev/faults", new { marketData = "flaky" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("validation_failed");
    }
}

public class DevFaultsAbsentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Dev_endpoints_do_not_exist_outside_Development()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/dev/faults", new { marketData = "down" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

/// <summary>The synced fixture market as a Development API with the prototype controls on.</summary>
public sealed class DevSyncedMarketApiFactory : SyncedMarketApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Features:DevFaultInjection"] = "true" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
    }
}

/// <summary><c>POST /dev/market/reseed</c> (T-110): fresh fixture history for today, so NVDA's breakout is a live signal again.</summary>
public class DevMarketReseedTests(DevSyncedMarketApiFactory factory) : IClassFixture<DevSyncedMarketApiFactory>
{
    [Fact]
    public async Task Reseeding_replaces_the_fake_bars_and_rescans_to_the_fixture_signals()
    {
        await factory.EnsureSyncedAsync();
        await factory.RefreshQuotesAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            // A day later: yesterday's synced bar shape no longer matches today's fixture history.
            await db.Database.ExecuteSqlRawAsync("UPDATE market.price_bars SET high = high * 1.05, close = close * 1.04");
        }

        var response = await factory.CreateClient().PostAsync("/dev/market/reseed", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storedBars").GetInt32().ShouldBeGreaterThan(1000);
        await using var check = factory.Services.CreateAsyncScope();
        var signals = check.ServiceProvider.GetRequiredService<DiscoveryDbContext>();
        (await signals.Signals.AnyAsync(s => s.Symbol == "NVDA" && s.Status == "active" && s.SignalType == "breakout")).ShouldBeTrue();
    }
}
