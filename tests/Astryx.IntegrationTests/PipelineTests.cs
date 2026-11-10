using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Astryx.IntegrationTests;

/// <summary>Shared request pipeline: problem details, correlation IDs, strict JSON, validation (T-013).</summary>
public class PipelineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe((int)expected);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("type").GetString().ShouldBe($"https://docs.astryx.app/errors/{code}");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem() =>
        await ProblemAsync(await _client.GetAsync("/api/v1/does-not-exist"), HttpStatusCode.NotFound, "not_found");

    [Fact]
    public async Task Valid_correlation_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "client-req-0001");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("client-req-0001");
    }

    [Fact]
    public async Task Malformed_correlation_id_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "bad id with spaces <script>");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldMatch("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Correlation_id_appears_in_problem_details()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        request.Headers.Add("X-Correlation-Id", "trace-me-12345");

        var problem = await ProblemAsync(await _client.SendAsync(request), HttpStatusCode.NotFound, "not_found");

        problem.GetProperty("correlationId").GetString().ShouldBe("trace-me-12345");
    }

    [Fact]
    public async Task Valid_request_round_trips_decimals_as_strings()
    {
        var response = await _client.PostAsync("/_test/echo", Json("""{"symbol":"NVDA","amount":"14174.00","entry":"186.50"}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"symbol":"NVDA","amount":"14174.00","entry":"186.50"}""");
    }

    [Theory]
    [InlineData("""{"symbol":"NVDA","amount":"1.00","entry":"186.50""")]                     // malformed JSON
    [InlineData("""{"symbol":"NVDA","amount":"1.00","entry":"186.50","extra":true}""")]      // unknown property
    [InlineData("""{"symbol":"NVDA","amount":1.00,"entry":"186.50"}""")]                     // money as JSON number
    [InlineData("""{"symbol":"NVDA","amount":"1.00","entry":"0"}""")]                        // zero price
    public async Task Bad_json_returns_400_problem(string body) =>
        await ProblemAsync(await _client.PostAsync("/_test/echo", Json(body)), HttpStatusCode.BadRequest, "bad_request");

    [Fact]
    public async Task Validation_failure_returns_422_with_field_errors()
    {
        var response = await _client.PostAsync("/_test/echo", Json("""{"symbol":"nvda!","amount":"-1","entry":"186.50"}"""));

        var problem = await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "validation_failed");
        var errors = problem.GetProperty("errors");
        errors.GetProperty("symbol").GetArrayLength().ShouldBe(1);
        errors.GetProperty("amount").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_without_leaking_details()
    {
        var response = await _client.GetAsync("/_test/throw");

        var problem = await ProblemAsync(response, HttpStatusCode.InternalServerError, "internal_error");
        problem.ToString().ShouldNotContain("boom");
    }

    [Theory]
    [InlineData("quote_stale", HttpStatusCode.Conflict)]
    [InlineData("risk_check_failed", HttpStatusCode.UnprocessableEntity)]
    [InlineData("live_trading_disabled", HttpStatusCode.Forbidden)]
    [InlineData("instrument_not_found", HttpStatusCode.NotFound)]
    [InlineData("scanner_unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task Domain_errors_map_to_documented_status_codes(string code, HttpStatusCode status) =>
        await ProblemAsync(await _client.GetAsync($"/_test/error/{code}"), status, code);

    [Fact]
    public async Task Body_over_64_kb_returns_413()
    {
        var big = new string('x', 70 * 1024);
        var response = await _client.PostAsync("/_test/echo", Json($$"""{"symbol":"NVDA","amount":"1","entry":"1","pad":"{{big}}"}"""));

        await ProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
    }
}
