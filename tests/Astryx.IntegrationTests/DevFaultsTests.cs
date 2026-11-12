using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.MarketData.Application.Providers;
using Microsoft.AspNetCore.Hosting;
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
