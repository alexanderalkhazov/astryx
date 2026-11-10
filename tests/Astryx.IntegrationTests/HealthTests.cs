using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Astryx.IntegrationTests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Live_returns_200()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_reports_postgres_healthy()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/health/ready");
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("entries").GetProperty("postgres").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task System_status_reports_unreachable_ai_as_degraded_and_live_trading_off()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/system/status");
        body.GetProperty("database").GetString().ShouldBe("healthy");
        body.GetProperty("ai").GetProperty("status").GetString().ShouldBe("degraded");
        body.GetProperty("features").GetProperty("liveTrading").GetBoolean().ShouldBeFalse();
        body.GetProperty("broker").GetProperty("mode").GetString().ShouldBe("paper");
    }
}
