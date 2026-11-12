using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Ai.Infrastructure.Ollama;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai;

/// <summary>Composition entry point for the Ai module (SYSTEM_DESIGN.md §7.7, §12).</summary>
public static class AiModule
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(AiDbContext.SchemaName, historyKey: "ai");
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<PromptOptions>().Configure(o =>
        {
            foreach (var entry in configuration.GetSection(PromptOptions.SectionName).GetChildren())
            {
                o.Active[entry.Key] = entry.Value ?? string.Empty;
            }
        });
        services.AddSingleton<PromptRegistry>();
        services.AddScoped<AnalysisContextBuilder>();
        services.AddSingleton<AiOutputValidator>();
        services.AddOptions<AiGenerationOptions>().Bind(configuration.GetSection(AiGenerationOptions.SectionName));
        services.AddScoped<AnalysisGenerator>();

        // `Ai:Provider`: Ollama (default) or Fake (tests, offline development).
        if (string.Equals(configuration["Ai:Provider"], "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<FakeAiProvider>();
            services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<FakeAiProvider>());
            return services;
        }

        services.AddOptions<OllamaOptions>().Bind(configuration.GetSection(OllamaOptions.SectionName));
        services.AddSingleton<OllamaCapabilities>();
        services.AddSingleton<OllamaModelInfoCache>();
        services.AddSingleton<CircuitBreaker>();
        services.AddHttpClient<OllamaAiProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds + 5); // the per-request timeout fires first
        });
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<OllamaAiProvider>());
        services.AddHostedService<OllamaCapabilityProbe>();
        return services;
    }
}
