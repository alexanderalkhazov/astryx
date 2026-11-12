using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai.Infrastructure.Ollama;

/// <summary>
/// Learns once at startup whether the model accepts <c>format</c> (SYSTEM_DESIGN.md §12.3, §4.5). Never blocks or fails
/// startup: an unreachable Ollama leaves the capability unknown, which is treated as unsupported.
/// </summary>
public sealed class OllamaCapabilityProbe(IServiceScopeFactory scopes, IOptions<OllamaOptions> options, OllamaCapabilities capabilities, ILogger<OllamaCapabilityProbe> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.SupportsStructuredOutput is { } configured)
        {
            capabilities.SupportsStructuredOutput = configured;
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<OllamaAiProvider>();
        var supported = await provider.ProbeStructuredOutputAsync(stoppingToken);
        capabilities.SupportsStructuredOutput = supported;
        logger.LogInformation(
            "Ollama model {Model}: structured output {Support}", options.Value.Model, supported switch { true => "supported", false => "unsupported (schema goes in the prompt)", _ => "unknown (Ollama unreachable)" });
    }
}
