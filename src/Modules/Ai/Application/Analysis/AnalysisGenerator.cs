using Astryx.Contracts.MarketData;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai.Application.Analysis;

/// <summary><c>Ai:Generation</c>: sampling and limits for analysis calls.</summary>
public sealed class AiGenerationOptions
{
    public const string SectionName = "Ai:Generation";

    public decimal Temperature { get; set; } = 0.2m;

    public int MaxOutputTokens { get; set; } = 1500;

    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>One model call and what the validator made of it.</summary>
public sealed record GenerationAttempt(AiCompletion Completion, ValidationResult Validation);

/// <summary>The final validation, every attempt (for <c>ai_requests</c>), and the model that answered.</summary>
public sealed record GenerationResult(ValidationResult Validation, IReadOnlyList<GenerationAttempt> Attempts, AiModelInfo Model);

/// <summary>
/// Calls the model and validates its output (SYSTEM_DESIGN.md §12.7). A parse or schema failure gets exactly one
/// repair retry with the error; anything else is final. Provider failures propagate as
/// <see cref="AiProviderUnavailableException"/>, so the job owns retries (§12.8).
/// </summary>
public sealed class AnalysisGenerator(IAiProvider provider, AiOutputValidator validator, IMarketDataFreshness freshness, TimeProvider timeProvider, IOptions<AiGenerationOptions> options)
{
    public const int MaxAttempts = 2;

    public async Task<GenerationResult> GenerateAsync(AnalysisContext context, PromptTemplate prompt, decimal minRewardRisk, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var model = await provider.GetModelInfoAsync(cancellationToken);
        var messages = prompt.Render(context).ToList();
        var attempts = new List<GenerationAttempt>();

        while (true)
        {
            var request = new AiCompletionRequest(model.Name, messages, prompt.Schema, settings.Temperature, settings.MaxOutputTokens, TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var completion = await provider.CompleteJsonAsync(request, cancellationToken);
            var fresh = await freshness.IsQuoteFreshAsync(context.DataAsOf, cancellationToken);
            var input = new ValidationInput(context, prompt, model.Name, timeProvider.GetUtcNow(), fresh, minRewardRisk);
            var validation = validator.Validate(completion.RawText, input);
            attempts.Add(new GenerationAttempt(completion, validation));

            if (validation.Status != ValidationStatus.Invalid || !validation.Repairable || attempts.Count >= MaxAttempts)
            {
                return new GenerationResult(validation, attempts, model);
            }

            var problem = validation.Issues[0].Code == ValidationIssueCodes.ParseError
                ? $"Your previous output was not valid JSON: {validation.FirstError}"
                : $"Your previous output did not match the schema: {string.Join("; ", validation.Issues.Select(i => i.Message))}";
            messages.Add(AiMessage.Assistant(Truncate(completion.RawText, 8000)));
            messages.Add(AiMessage.User($"{problem}\nReply with only the corrected JSON object."));
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
