using System.Net;
using System.Text;
using System.Text.Json;
using Astryx.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Microsoft.Extensions.Options;

namespace Astryx.UnitTests.Paper;

/// <summary>Alpaca Trading API contract (SYSTEM_DESIGN.md §17.3–17.4, TASKS.md T-140, T-143), against documented shapes.</summary>
public class AlpacaTradingClientTests
{
    private sealed class StubAlpaca(Func<HttpRequestMessage, string?, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            var (status, text) = answer(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly AlpacaCredentials Keys = new("PKTEST00003F2A", "secret-value");

    private static (AlpacaTradingClient Client, StubAlpaca Stub) Create(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> answer)
    {
        var stub = new StubAlpaca(answer);
        var options = Options.Create(new AlpacaOptions());
        return (new AlpacaTradingClient(stub, new AlpacaRateLimiter(options), options), stub);
    }

    private const string BracketOrder = """
        {"id":"61e69015-8549-4bfd-b9c3-01e75843f47d","client_order_id":"0193b8e5-0000-7000-8000-000000000001","status":"accepted","symbol":"NVDA",
         "side":"buy","type":"stop","order_class":"bracket","qty":"64","filled_qty":"0","filled_avg_price":null,"stop_price":"186.5","limit_price":null,
         "legs":[{"id":"leg-tp","client_order_id":"tp","status":"held","symbol":"NVDA","side":"sell","type":"limit","order_class":"bracket","qty":"64","filled_qty":"0","limit_price":"204","legs":null},
                 {"id":"leg-sl","client_order_id":"sl","status":"held","symbol":"NVDA","side":"sell","type":"stop","order_class":"bracket","qty":"64","filled_qty":"0","stop_price":"178.2","legs":null}]}
        """;

    [Fact]
    public async Task A_bracket_is_posted_to_the_paper_host_with_both_exits_and_the_client_order_id()
    {
        var (client, stub) = Create((_, _) => (HttpStatusCode.OK, BracketOrder));

        var order = await client.SubmitBracketAsync(Keys, new AlpacaBracketRequest("NVDA", 64m, "stop", 186.50m, null, 178.20m, 204.00m, "0193b8e5-0000-7000-8000-000000000001"), default);

        var (request, body) = stub.Calls.Single();
        (request.Method, request.RequestUri!.ToString()).ShouldBe((HttpMethod.Post, "https://paper-api.alpaca.markets/v2/orders"));
        request.Headers.GetValues("APCA-API-KEY-ID").Single().ShouldBe("PKTEST00003F2A");
        request.Headers.GetValues("APCA-API-SECRET-KEY").Single().ShouldBe("secret-value");
        var json = JsonDocument.Parse(body!).RootElement;
        (json.GetProperty("qty").GetString(), json.GetProperty("type").GetString(), json.GetProperty("time_in_force").GetString(), json.GetProperty("order_class").GetString())
            .ShouldBe(("64", "stop", "gtc", "bracket")); // GTC so the exit legs outlive the session
        (json.GetProperty("stop_price").GetString(), json.GetProperty("stop_loss").GetProperty("stop_price").GetString(), json.GetProperty("take_profit").GetProperty("limit_price").GetString())
            .ShouldBe(("186.5", "178.2", "204"));
        json.TryGetProperty("limit_price", out _).ShouldBeFalse(); // nulls are left out, not sent
        json.GetProperty("client_order_id").GetString().ShouldBe("0193b8e5-0000-7000-8000-000000000001");
        (order.Id, order.Status, order.Quantity, order.Legs.Count).ShouldBe(("61e69015-8549-4bfd-b9c3-01e75843f47d", "accepted", 64m, 2));
        order.Legs.Single(l => l.Type == "stop").StopPrice.ShouldBe(178.2m);
    }

    [Fact]
    public async Task Order_lookups_ask_for_nested_legs_and_a_missing_order_is_null()
    {
        var (client, stub) = Create((request, _) => request.RequestUri!.AbsolutePath.EndsWith("missing", StringComparison.Ordinal) || request.RequestUri.Query.Contains("unknown")
            ? (HttpStatusCode.NotFound, """{"message":"order not found"}""")
            : (HttpStatusCode.OK, BracketOrder));

        (await client.GetOrderAsync(Keys, "61e69015", default))!.Legs.Count.ShouldBe(2);
        (await client.GetOrderByClientIdAsync(Keys, "0193b8e5-0000-7000-8000-000000000001", default)).ShouldNotBeNull();
        (await client.GetOrderAsync(Keys, "missing", default)).ShouldBeNull();
        (await client.GetOrderByClientIdAsync(Keys, "unknown", default)).ShouldBeNull();
        stub.Calls[0].Request.RequestUri!.Query.ShouldContain("nested=true");
        stub.Calls[1].Request.RequestUri!.PathAndQuery.ShouldStartWith("/v2/orders:by_client_order_id?client_order_id=");
    }

    [Fact]
    public async Task A_filled_order_carries_Alpacas_price_quantity_and_time()
    {
        var (client, _) = Create((_, _) => (HttpStatusCode.OK, """
            {"id":"o1","client_order_id":"c1","status":"filled","symbol":"NVDA","side":"buy","type":"market","order_class":"simple","qty":"64","filled_qty":"64",
             "filled_avg_price":"186.7123","filled_at":"2026-11-17T14:35:01.123Z","legs":null}
            """));

        var order = await client.GetOrderAsync(Keys, "o1", default);

        (order!.IsFilled, order.FilledQuantity, order.FilledAvgPrice, order.FilledAt).ShouldBe((true, 64m, 186.7123m, DateTimeOffset.Parse("2026-11-17T14:35:01.123Z", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Account_positions_and_close_use_the_documented_paths()
    {
        var (client, stub) = Create((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v2/account" => (HttpStatusCode.OK, """{"account_number":"PA3TEST","status":"ACTIVE","cash":"99975.24","equity":"99975.24","buying_power":"399900.96","trading_blocked":false,"account_blocked":false}"""),
            "/v2/positions" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, """[{"symbol":"NVDA","asset_id":"4ce9353c","qty":"64","avg_entry_price":"186.71","current_price":"190.10"}]"""),
            _ => (HttpStatusCode.OK, """{"id":"close-1","client_order_id":"x","status":"accepted","symbol":"NVDA","side":"sell","type":"market","order_class":"simple","qty":"64","filled_qty":"0"}"""),
        });

        var account = await client.GetAccountAsync(Keys, default);
        var positions = await client.ListPositionsAsync(Keys, default);
        var closing = await client.ClosePositionAsync(Keys, "BRK.B", default);

        (account.AccountNumber, account.Cash, account.TradingBlocked).ShouldBe(("PA3TEST", 99975.24m, false));
        positions.Single().ShouldBe(new AlpacaPosition("NVDA", "4ce9353c", 64m, 186.71m, 190.10m));
        (closing.Side, closing.Status).ShouldBe(("sell", "accepted"));
        stub.Calls[2].Request.Method.ShouldBe(HttpMethod.Delete);
        stub.Calls[2].Request.RequestUri!.AbsolutePath.ShouldBe("/v2/positions/BRK.B");
    }

    [Fact]
    public async Task Fill_activities_are_read_oldest_first_and_paged_by_the_last_id()
    {
        var page = Enumerable.Range(1, 100).Select(i => $$"""{"id":"a{{i}}","activity_type":"FILL","order_id":"o1","symbol":"NVDA","side":"buy","qty":"1","price":"186.5","transaction_time":"2026-11-18T14:35:01Z"}""");
        var (client, stub) = Create((request, _) => (HttpStatusCode.OK, request.RequestUri!.Query.Contains("page_token")
            ? """[{"id":"a101","activity_type":"FILL","type":"partial_fill","order_id":"o2","symbol":"AAPL","side":"sell","qty":"3","price":"351.9","transaction_time":"2026-11-18T15:00:00Z"}]"""
            : $"[{string.Join(',', page)}]"));

        var fills = await client.ListFillsAsync(Keys, DateTimeOffset.Parse("2026-11-18T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), default);

        fills.Count.ShouldBe(101);
        fills[^1].ShouldBe(new AlpacaFill("a101", "o2", "AAPL", "sell", 3m, 351.9m, DateTimeOffset.Parse("2026-11-18T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        stub.Calls[0].Request.RequestUri!.PathAndQuery.ShouldBe("/v2/account/activities/FILL?after=2026-11-18T00%3A00%3A00Z&direction=asc&page_size=100");
        stub.Calls[1].Request.RequestUri!.Query.ShouldEndWith("&page_token=a100");
    }

    [Fact]
    public void The_stream_reads_authorization_and_trade_updates_and_ignores_the_rest()
    {
        static byte[] Utf8(string json) => System.Text.Encoding.UTF8.GetBytes(json);

        AlpacaTradeUpdatesStream.StreamUrl(new Uri("https://paper-api.alpaca.markets/")).ToString().ShouldBe("wss://paper-api.alpaca.markets/stream");
        AlpacaTradeUpdatesStream.IsAuthorized(Utf8("""{"stream":"authorization","data":{"action":"authenticate","status":"authorized"}}""")).ShouldBeTrue();
        AlpacaTradeUpdatesStream.IsAuthorized(Utf8("""{"stream":"authorization","data":{"action":"authenticate","status":"unauthorized"}}""")).ShouldBeFalse();
        AlpacaTradeUpdatesStream.Parse(Utf8("""{"stream":"listening","data":{"streams":["trade_updates"]}}""")).ShouldBeNull();

        var update = AlpacaTradeUpdatesStream.Parse(Utf8("""
            {"stream":"trade_updates","data":{"event":"fill","execution_id":"x1","price":"186.71","qty":"64","position_qty":"64",
             "timestamp":"2026-11-19T14:35:01.123Z","order":{"id":"61e69015","client_order_id":"0193b8e5-0000-7000-8000-000000000001","status":"filled","symbol":"NVDA"}}}
            """));

        update.ShouldBe(new AlpacaTradeUpdate("fill", "61e69015", "0193b8e5-0000-7000-8000-000000000001", DateTimeOffset.Parse("2026-11-19T14:35:01.123Z", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Cancelling_an_order_Alpaca_already_finished_is_not_an_error()
    {
        var (client, _) = Create((_, _) => (HttpStatusCode.UnprocessableEntity, """{"message":"order is already in \"filled\" state"}"""));

        await Should.NotThrowAsync(() => client.CancelOrderAsync(Keys, "o1", default));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, AlpacaFailure.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AlpacaFailure.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, AlpacaFailure.CredentialsInvalid)]
    public async Task Failures_map_to_app_failures_without_the_secret(HttpStatusCode status, AlpacaFailure expected)
    {
        var (client, _) = Create((_, _) => (status, """{"message":"nope"}"""));

        var error = await Should.ThrowAsync<AlpacaApiException>(() => client.GetAccountAsync(Keys, default));

        error.Failure.ShouldBe(expected);
        error.Message.ShouldNotContain("secret-value");
    }

    [Fact]
    public async Task A_malformed_answer_is_unavailable_not_a_crash()
    {
        var (client, _) = Create((_, _) => (HttpStatusCode.OK, "<html>maintenance</html>"));

        (await Should.ThrowAsync<AlpacaApiException>(() => client.GetAccountAsync(Keys, default))).Failure.ShouldBe(AlpacaFailure.Unavailable);
    }
}
