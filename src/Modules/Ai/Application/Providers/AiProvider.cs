using System.Text.Json;

namespace Astryx.Modules.Ai.Application.Providers;

public sealed record AiMessage(string Role, string Content)
{
    public static AiMessage System(string content) => new("system", content);

    public static AiMessage User(string content) => new("user", content);

    public static AiMessage Assistant(string content) => new("assistant", content);
}

/// <summary>A JSON completion request (SYSTEM_DESIGN.md §12.3).</summary>
/// <param name="OutputSchema">Sent as Ollama <c>format</c> when the model supports structured output; otherwise embedded in the prompt.</param>
/// <param name="OutputExample">
/// A filled-in skeleton of the expected JSON. Without structured output, small models follow it far better than the
/// schema itself, so a provider embeds it instead of the schema when it is set.
/// </param>
public sealed record AiCompletionRequest(
    string Model, IReadOnlyList<AiMessage> Messages, JsonDocument OutputSchema, decimal Temperature, int MaxOutputTokens, TimeSpan Timeout, string? OutputExample = null);

/// <param name="FinishReason">e.g. <c>stop</c>, or <c>length</c> when <c>MaxOutputTokens</c> cut the answer off.</param>
public sealed record AiCompletion(string RawText, int? PromptTokens, int? CompletionTokens, TimeSpan Latency, string Model, string? ModelDigest, string FinishReason);

/// <param name="ContextLength">Tokens the model accepts, when the provider reports it.</param>
public sealed record AiModelInfo(string Name, string? Digest, int? ContextLength, string? ParameterSize, string? Quantization);

/// <summary>
/// The provider can't answer right now: unreachable, timed out, a 5xx, a missing model, or the circuit breaker is open
/// (error code <c>ai_provider_unavailable</c>, §12.8). Jobs decide whether to retry.
/// </summary>
public sealed class AiProviderUnavailableException(string reason, Exception? inner = null) : Exception($"AI provider unavailable: {reason}", inner)
{
    public string Reason { get; } = reason;
}

/// <summary>A model behind a provider (SYSTEM_DESIGN.md §12.3): Ollama locally, a deterministic fake in tests.</summary>
public interface IAiProvider
{
    /// <summary><c>ollama</c> or <c>fake</c>.</summary>
    string Name { get; }

    Task<AiModelInfo> GetModelInfoAsync(CancellationToken cancellationToken);

    /// <exception cref="AiProviderUnavailableException">The provider could not produce a completion.</exception>
    Task<AiCompletion> CompleteJsonAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}
