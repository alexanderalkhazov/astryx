using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Astryx.IntegrationTests;

/// <summary>Search and recents (SYSTEM_DESIGN.md §8.4.12, §11.4 M1–M3, T-046).</summary>
public class SearchTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<JsonElement>> SearchAsync(string q, int? limit = null)
    {
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/search?q={Uri.EscapeDataString(q)}{(limit is null ? "" : $"&limit={limit}")}");
        return body.GetProperty("items").EnumerateArray().ToList();
    }

    private static List<string> Symbols(IEnumerable<JsonElement> items) => items.Select(i => i.GetProperty("symbol").GetString()!).ToList();

    [Fact]
    public async Task Am_finds_Amazon_and_AMD_by_ticker_prefix_largest_first_without_name_noise()
    {
        var items = await SearchAsync("am");
        var symbols = Symbols(items);

        symbols.Take(2).ShouldBe(["AMZN", "AMD"]); // prefix matches by market cap
        symbols.ShouldNotContain(s => s.StartsWith("ZZ", StringComparison.Ordinal)); // "Sample" contains "am" but not at a word start
        var amd = items.Single(i => i.GetProperty("symbol").GetString() == "AMD");
        amd.GetProperty("name").GetString().ShouldBe("Advanced Micro Devices");
        amd.GetProperty("last").GetString().ShouldBe("168.25");
        amd.GetProperty("changePct").GetString().ShouldBe("0.0218");
        amd.GetProperty("held").GetBoolean().ShouldBeFalse();
        amd.GetProperty("signalType").ValueKind.ShouldBe(JsonValueKind.Null);
        items.Single(i => i.GetProperty("symbol").GetString() == "AMZN").GetProperty("last").GetString().ShouldBe("191.35"); // design 19
    }

    [Theory]
    [InlineData("nvda", "NVDA")] // exact ticker, any case
    [InlineData("NVIDIA", "NVDA")] // company name
    [InlineData("amazon", "AMZN")]
    [InlineData("devices", "AMD")] // a word start inside the name ("Advanced Micro Devices")
    [InlineData("salesforce", "CRM")]
    public async Task Finds_by_ticker_or_company_name(string query, string expectedFirst)
    {
        Symbols(await SearchAsync(query))[0].ShouldBe(expectedFirst);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("xyzzy")]
    public async Task Wildcards_are_literal_and_misses_are_empty(string query)
    {
        (await SearchAsync(query)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api/v1/search?q=")]
    [InlineData("/api/v1/search?q=%20%20")]
    [InlineData("/api/v1/search?q=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 41 characters
    [InlineData("/api/v1/search?q=am&limit=51")]
    public async Task Invalid_queries_are_400(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Limit_caps_the_results()
    {
        (await SearchAsync("a", limit: 3)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Recents_are_newest_first_deduplicated_and_capped_at_ten()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        foreach (var symbol in new[] { "NVDA", "AMD", "TSLA", "COST" })
        {
            (await client.PostAsJsonAsync("/api/v1/search/recent", new { symbol })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        Symbols(await RecentAsync(client)).ShouldBe(["COST", "TSLA", "AMD", "NVDA"]);

        await client.PostAsJsonAsync("/api/v1/search/recent", new { symbol = "nvda" }); // again, any case
        Symbols(await RecentAsync(client)).ShouldBe(["NVDA", "COST", "TSLA", "AMD"]);

        foreach (var symbol in new[] { "AAPL", "MSFT", "JPM", "LLY", "UBER", "CRM", "PLTR", "AVGO" })
        {
            await client.PostAsJsonAsync("/api/v1/search/recent", new { symbol });
        }

        var recent = Symbols(await RecentAsync(client));
        recent.Count.ShouldBe(10);
        recent[0].ShouldBe("AVGO");
        recent.ShouldNotContain("AMD"); // the oldest two fell off
        recent.ShouldNotContain("TSLA");
    }

    [Fact]
    public async Task Recents_belong_to_their_user_and_need_a_known_symbol()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        await alice.PostAsJsonAsync("/api/v1/search/recent", new { symbol = "NVDA" });

        (await RecentAsync(bob)).ShouldBeEmpty();
        var unknown = await alice.PostAsJsonAsync("/api/v1/search/recent", new { symbol = "NOPE" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("instrument_not_found");
    }

    [Fact]
    public async Task Search_requires_a_session()
    {
        (await factory.CreateClient().GetAsync("/api/v1/search?q=am")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<List<JsonElement>> RecentAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/search/recent")).GetProperty("items").EnumerateArray().ToList();
}
