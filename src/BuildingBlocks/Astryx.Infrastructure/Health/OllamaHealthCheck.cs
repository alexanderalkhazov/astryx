using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Astryx.Infrastructure.Health;

/// <summary>
/// Reports AI as Degraded (never Unhealthy) when Ollama or the configured model is missing:
/// AI is research-only and must not take the API out of rotation (SYSTEM_DESIGN.md §12.3).
/// </summary>
public sealed class OllamaHealthCheck(HttpClient httpClient, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var model = configuration["Ai:Ollama:Model"] ?? "";
        var data = new Dictionary<string, object> { ["baseUrl"] = httpClient.BaseAddress?.ToString() ?? "", ["model"] = model };
        try
        {
            var tags = await httpClient.GetFromJsonAsync<OllamaTags>("api/tags", cancellationToken);
            var present = tags?.Models?.Any(m => m.Name == model) == true;
            data["modelPresent"] = present;
            return present
                ? HealthCheckResult.Healthy("Ollama reachable, model present", data)
                : HealthCheckResult.Degraded($"Ollama reachable, model '{model}' not pulled", data: data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return HealthCheckResult.Degraded("Ollama unreachable", ex, data);
        }
    }

    private sealed record OllamaTags(List<OllamaModel>? Models);
    private sealed record OllamaModel(string Name);
}
