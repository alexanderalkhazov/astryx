using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Astryx.Modules.Ai.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai.Infrastructure.Ollama;

/// <summary>
/// <see cref="IAiProvider"/> over a local Ollama (SYSTEM_DESIGN.md §12.3): <c>POST /api/chat</c> with
/// <c>stream:false</c> and <c>think:false</c>. The output schema goes in <c>format</c> only when the model supports
/// structured output (probed at startup; <c>qwen3.5:0.8b</c> does not, §4.5); otherwise it is embedded in the system
/// prompt and the validator is the guarantee (§12.7). No HTTP retries; a circuit breaker fails fast after repeated
/// failures.
/// </summary>
public sealed class OllamaAiProvider(
    HttpClient http, IOptions<OllamaOptions> options, OllamaCapabilities capabilities, CircuitBreaker breaker, OllamaModelInfoCache cache, TimeProvider timeProvider,
    ILogger<OllamaAiProvider> logger)
    : IAiProvider
{
    private static readonly TimeSpan ModelInfoLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Name => "ollama";

    public async Task<AiModelInfo> GetModelInfoAsync(CancellationToken cancellationToken)
    {
        var model = options.Value.Model;
        if (cache.Get(model, timeProvider.GetUtcNow()) is { } cached)
        {
            return cached;
        }

        try
        {
            var tags = await http.GetFromJsonAsync<TagsResponse>("api/tags", Json, cancellationToken);
            var tag = tags?.Models?.FirstOrDefault(m => m.Name == model) ?? throw new AiProviderUnavailableException("model_missing");
            using var showResponse = await http.PostAsJsonAsync("api/show", new { model }, Json, cancellationToken);
            int? contextLength = null;
            if (showResponse.IsSuccessStatusCode)
            {
                var show = await showResponse.Content.ReadFromJsonAsync<JsonObject>(Json, cancellationToken);
                contextLength = ContextLength(show?["model_info"] as JsonObject);
            }

            var info = new AiModelInfo(model, tag.Digest, contextLength, tag.Details?.ParameterSize, tag.Details?.QuantizationLevel);
            cache.Set(info, timeProvider.GetUtcNow() + ModelInfoLifetime);

            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new AiProviderUnavailableException("unreachable", ex);
        }
    }

    public async Task<AiCompletion> CompleteJsonAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        breaker.EnsureClosed();
        var structured = capabilities.SupportsStructuredOutput == true;
        var body = new ChatRequest(
            request.Model,
            structured ? request.Messages.Select(ToWire).ToList() : WithEmbeddedSchema(request.Messages, request.OutputSchema),
            Stream: false,
            Think: false,
            Format: structured ? JsonNode.Parse(request.OutputSchema.RootElement.GetRawText()) : null,
            Options: new ChatOptions(request.Temperature, request.MaxOutputTokens),
            KeepAlive: options.Value.KeepAlive);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await http.PostAsJsonAsync("api/chat", body, Json, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotImplemented && structured)
            {
                // The model lost structured-output support (a different model behind the same name): fall back.
                capabilities.SupportsStructuredOutput = false;
                logger.LogWarning("Ollama model {Model} rejected `format`; embedding the schema in the prompt from now on", request.Model);
                return await CompleteJsonAsync(request, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                breaker.RecordFailure();
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AiProviderUnavailableException($"http_{(int)response.StatusCode}: {Truncate(detail)}");
            }

            var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, timeout.Token) ?? throw new AiProviderUnavailableException("empty_response");
            breaker.RecordSuccess();
            return new AiCompletion(
                chat.Message?.Content ?? string.Empty,
                chat.PromptEvalCount,
                chat.EvalCount,
                Stopwatch.GetElapsedTime(started),
                chat.Model ?? request.Model,
                await DigestAsync(cancellationToken),
                chat.DoneReason ?? (chat.Done ? "stop" : "unknown"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            breaker.RecordFailure();
            throw new AiProviderUnavailableException("timeout");
        }
        catch (HttpRequestException ex)
        {
            breaker.RecordFailure();
            throw new AiProviderUnavailableException("unreachable", ex);
        }
        catch (JsonException ex)
        {
            breaker.RecordFailure();
            throw new AiProviderUnavailableException("malformed_response", ex);
        }
    }

    /// <summary>
    /// Startup probe (§12.3): a one-token request with a minimal schema in <c>format</c>. 200 means supported;
    /// <c>501 structured output is unavailable</c> means not. Anything else leaves it unknown (treated as unsupported).
    /// </summary>
    public async Task<bool?> ProbeStructuredOutputAsync(CancellationToken cancellationToken)
    {
        var body = new ChatRequest(
            options.Value.Model, [new WireMessage("user", "Reply with {}.")], Stream: false, Think: false,
            Format: new JsonObject { ["type"] = "object" }, Options: new ChatOptions(0m, 1), KeepAlive: options.Value.KeepAlive);
        try
        {
            using var response = await http.PostAsJsonAsync("api/chat", body, Json, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => true,
                HttpStatusCode.NotImplemented => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<string?> DigestAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await GetModelInfoAsync(cancellationToken)).Digest;
        }
        catch (AiProviderUnavailableException)
        {
            return null; // the completion itself worked; a missing digest is recorded as unknown
        }
    }

    /// <summary>Without <c>format</c>, the schema becomes part of the system prompt.</summary>
    private static List<WireMessage> WithEmbeddedSchema(IReadOnlyList<AiMessage> messages, JsonDocument schema)
    {
        var instruction = "Respond with a single JSON object only: no prose, no markdown fences. It must match this JSON Schema:\n" + schema.RootElement.GetRawText();
        var wire = messages.Select(ToWire).ToList();
        var system = wire.FindIndex(m => m.Role == "system");
        if (system >= 0)
        {
            wire[system] = wire[system] with { Content = wire[system].Content + "\n\n" + instruction };
        }
        else
        {
            wire.Insert(0, new WireMessage("system", instruction));
        }

        return wire;
    }

    private static int? ContextLength(JsonObject? modelInfo)
    {
        var entry = modelInfo?.FirstOrDefault(p => p.Key.EndsWith(".context_length", StringComparison.Ordinal));
        return entry?.Value is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
    }

    private static WireMessage ToWire(AiMessage m) => new(m.Role, m.Content);

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];

    private sealed record WireMessage(string Role, string Content);

    private sealed record ChatOptions(decimal Temperature, [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record ChatRequest(
        string Model, List<WireMessage> Messages, bool Stream, bool Think, JsonNode? Format, ChatOptions Options, [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record ChatResponse(
        string? Model,
        WireMessage? Message,
        bool Done,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    private sealed record TagsResponse(List<TagModel>? Models);

    private sealed record TagModel(string Name, string? Digest, TagDetails? Details);

    private sealed record TagDetails([property: JsonPropertyName("parameter_size")] string? ParameterSize, [property: JsonPropertyName("quantization_level")] string? QuantizationLevel);
}
