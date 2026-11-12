using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.MarketData;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Watchlists W1–W8 and the star toggle (SYSTEM_DESIGN.md §7.5, §11.5, T-047).</summary>
public class WatchlistTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<List<JsonElement>> ListsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/watchlists")).GetProperty("items").EnumerateArray().ToList();

    private static async Task<JsonElement> DefaultAsync(HttpClient client) => (await ListsAsync(client)).Single(l => l.GetProperty("isDefault").GetBoolean());

    private static List<string> Symbols(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("symbol").GetString()!).ToList();

    private static string ETag(JsonElement list) => $"\"v{list.GetProperty("version").GetInt32()}\"";

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    private static HttpRequestMessage WithIfMatch(HttpMethod method, string path, object body, string? etag)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return request;
    }

    [Fact]
    public async Task A_new_user_has_one_empty_default_list()
    {
        var lists = await ListsAsync(_client);

        lists.Count.ShouldBe(1);
        lists[0].GetProperty("name").GetString().ShouldBe("Watchlist");
        lists[0].GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        Symbols(lists[0]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Star_is_idempotent_and_items_carry_quote_summaries()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await client.PutAsync("/api/v1/watchlists/default/items/AMD", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsync("/api/v1/watchlists/default/items/amd", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent); // again, any case
        (await client.PutAsync("/api/v1/watchlists/default/items/COST", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await DefaultAsync(client);
        Symbols(list).ShouldBe(["AMD", "COST"]); // in the order added
        var amd = list.GetProperty("items")[0];
        amd.GetProperty("name").GetString().ShouldBe("Advanced Micro Devices");
        amd.GetProperty("last").GetString().ShouldBe("168.25");
        amd.GetProperty("changePct").GetString().ShouldBe("0.0218");
        amd.GetProperty("hasActiveSignal").GetBoolean().ShouldBeFalse();
        amd.GetProperty("held").GetBoolean().ShouldBeFalse();
        amd.GetProperty("addedAt").GetString().ShouldEndWith("Z");
    }

    [Fact]
    public async Task Star_off_is_idempotent_and_unknown_symbols_are_404_to_star()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await client.PutAsync("/api/v1/watchlists/default/items/NVDA", null);

        (await client.DeleteAsync("/api/v1/watchlists/default/items/NVDA")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync("/api/v1/watchlists/default/items/NVDA")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync("/api/v1/watchlists/default/items/NOPE")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Symbols(await DefaultAsync(client)).ShouldBeEmpty();

        var unknown = await client.PutAsync("/api/v1/watchlists/default/items/NOPE", null);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(unknown)).ShouldBe("instrument_not_found");
    }

    [Fact]
    public async Task Item_changes_bump_the_version()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var before = (await DefaultAsync(client)).GetProperty("version").GetInt32();

        await client.PutAsync("/api/v1/watchlists/default/items/TSLA", null);
        await client.PutAsync("/api/v1/watchlists/default/items/TSLA", null); // no-op
        await client.DeleteAsync("/api/v1/watchlists/default/items/TSLA");

        (await DefaultAsync(client)).GetProperty("version").GetInt32().ShouldBe(before + 2);
    }

    [Fact]
    public async Task Adding_an_item_returns_it_and_a_duplicate_is_409()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        var created = await client.PostAsJsonAsync("/api/v1/watchlists/default/items", new { symbol = "JPM", note = "  rates play  " });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        item.GetProperty("symbol").GetString().ShouldBe("JPM");
        item.GetProperty("note").GetString().ShouldBe("rates play");

        var duplicate = await client.PostAsJsonAsync("/api/v1/watchlists/default/items", new { symbol = "jpm" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(duplicate)).ShouldBe("duplicate");

        var longNote = await client.PostAsJsonAsync("/api/v1/watchlists/default/items", new { symbol = "AAPL", note = new string('x', 281) });
        longNote.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Lists_can_be_created_renamed_and_deleted_but_not_the_default()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var created = await client.PostAsJsonAsync("/api/v1/watchlists", new { name = " Semis " });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.ETag!.Tag.ShouldBe("\"v1\"");
        var semis = await created.Content.ReadFromJsonAsync<JsonElement>();
        semis.GetProperty("name").GetString().ShouldBe("Semis");
        var id = semis.GetProperty("id").GetString();

        (await client.PostAsJsonAsync("/api/v1/watchlists", new { name = "Semis" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/api/v1/watchlists", new { name = "" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync("/api/v1/watchlists", new { name = new string('x', 41) })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var renamed = await client.SendAsync(WithIfMatch(HttpMethod.Patch, $"/api/v1/watchlists/{id}", new { name = "Chips" }, "\"v1\""));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        renamed.Headers.ETag!.Tag.ShouldBe("\"v2\"");
        (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().ShouldBe("Chips");

        (await ListsAsync(client)).Select(l => l.GetProperty("name").GetString()).ShouldBe(["Watchlist", "Chips"]); // default first
        (await client.PostAsJsonAsync($"/api/v1/watchlists/{id}/items", new { symbol = "NVDA" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.DeleteAsync($"/api/v1/watchlists/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/watchlists/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var defaultId = (await DefaultAsync(client)).GetProperty("id").GetString();
        var deleteDefault = await client.DeleteAsync($"/api/v1/watchlists/{defaultId}");
        deleteDefault.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(deleteDefault)).ShouldBe("conflict");
    }

    [Fact]
    public async Task Updates_need_the_current_etag()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var list = await DefaultAsync(client);
        var path = $"/api/v1/watchlists/{list.GetProperty("id").GetString()}";

        (await client.SendAsync(WithIfMatch(HttpMethod.Patch, path, new { name = "Main" }, null))).StatusCode.ShouldBe((HttpStatusCode)428);
        (await client.SendAsync(WithIfMatch(HttpMethod.Patch, path, new { name = "Main" }, "\"v999\""))).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await client.SendAsync(WithIfMatch(HttpMethod.Patch, path, new { name = "Main" }, "garbage"))).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await client.SendAsync(WithIfMatch(HttpMethod.Patch, path, new { name = "Main" }, ETag(list)))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Items_can_be_reordered_with_the_exact_set()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        foreach (var symbol in new[] { "NVDA", "AMD", "CRM" })
        {
            await client.PutAsync($"/api/v1/watchlists/default/items/{symbol}", null);
        }

        var list = await DefaultAsync(client);
        var path = $"/api/v1/watchlists/{list.GetProperty("id").GetString()}/order";

        var missing = await client.SendAsync(WithIfMatch(HttpMethod.Put, path, new { symbols = new[] { "CRM", "NVDA" } }, ETag(list)));
        missing.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var repeated = await client.SendAsync(WithIfMatch(HttpMethod.Put, path, new { symbols = new[] { "CRM", "CRM", "NVDA" } }, ETag(list)));
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var reordered = await client.SendAsync(WithIfMatch(HttpMethod.Put, path, new { symbols = new[] { "crm", "NVDA", "AMD" } }, ETag(list)));
        reordered.StatusCode.ShouldBe(HttpStatusCode.OK);
        Symbols(await reordered.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe(["CRM", "NVDA", "AMD"]);
        Symbols(await DefaultAsync(client)).ShouldBe(["CRM", "NVDA", "AMD"]);

        var stale = await client.SendAsync(WithIfMatch(HttpMethod.Put, path, new { symbols = new[] { "AMD", "NVDA", "CRM" } }, ETag(list)));
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Watched_symbols_are_quote_priorities()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await client.PutAsync("/api/v1/watchlists/default/items/UBER", null);

        await using var scope = factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<IPrioritySymbolSource>().Single(s => s.Priority == 2);
        (await source.GetSymbolsAsync(CancellationToken.None)).ShouldContain("UBER");
    }

    [Fact]
    public async Task Watchlists_require_a_session()
    {
        (await factory.CreateClient().GetAsync("/api/v1/watchlists")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
