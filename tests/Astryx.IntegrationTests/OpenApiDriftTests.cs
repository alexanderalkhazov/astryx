using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace Astryx.IntegrationTests;

/// <summary>The API as the local stack runs it (Development, prototype controls on), for its OpenAPI document.</summary>
public sealed class DevelopmentApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Features:DevFaultInjection"] = "true" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
    }
}

/// <summary>
/// The committed contract matches the code (TASKS.md T-010): changing a DTO or an endpoint without `pnpm api:gen` fails
/// here, and CI's mobile job then checks the generated TypeScript matches the committed document.
/// </summary>
public class OpenApiDriftTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task The_committed_openapi_document_matches_the_running_API()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var live = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        live.Remove("servers"); // host-specific; api-gen drops it too

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "apps", "api", "openapi.json")));

        JsonNode.DeepEquals(live, committed).ShouldBeTrue("apps/api/openapi.json is out of date: run `pnpm stack:up && pnpm api:gen` and commit the result.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Astryx.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Astryx.slnx not found above the test binaries.");
    }
}
