using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astryx.Modules.Ai.Application.Providers;

namespace Astryx.Modules.Ai.Infrastructure.Fake;

/// <summary>Canned model behaviours for tests and offline development (SYSTEM_DESIGN.md §12.3, §21.8).</summary>
public enum FakeAiScenario
{
    /// <summary>A well-formed <c>analysis.v1</c> for the NVDA design fixture.</summary>
    Valid,

    /// <summary>Prose and a markdown fence around JSON that does not parse.</summary>
    InvalidJson,

    /// <summary>Parses, but required fields are missing and types are wrong.</summary>
    SchemaError,

    /// <summary>Well-formed, but with numbers that contradict the input data (levels, RSI) and a forbidden field.</summary>
    HallucinatedNumbers,

    /// <summary>Takes longer than the request timeout.</summary>
    Timeout,

    /// <summary>The server answers 500.</summary>
    ServerError,
}

/// <summary>
/// Deterministic <see cref="IAiProvider"/>: answers from embedded fixtures. Tests script a sequence with
/// <see cref="Enqueue"/> (e.g. invalid JSON, then valid, for the repair retry); otherwise <see cref="DefaultScenario"/>.
/// Every request is recorded so tests can inspect prompts.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    public const string ModelName = "fake-analyst";
    public const string Digest = "sha256:fake";

    private readonly ConcurrentQueue<FakeAiScenario> _script = new();

    public string Name => "fake";

    public FakeAiScenario DefaultScenario { get; set; } = FakeAiScenario.Valid;

    public ConcurrentQueue<AiCompletionRequest> Requests { get; } = new();

    public void Enqueue(params FakeAiScenario[] scenarios)
    {
        foreach (var scenario in scenarios)
        {
            _script.Enqueue(scenario);
        }
    }

    public Task<AiModelInfo> GetModelInfoAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiModelInfo(ModelName, Digest, 32_768, "0.8B", "Q4_K_M"));

    public async Task<AiCompletion> CompleteJsonAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var scenario = _script.TryDequeue(out var next) ? next : DefaultScenario;
        switch (scenario)
        {
            case FakeAiScenario.Timeout:
                try
                {
                    await Task.Delay(request.Timeout + TimeSpan.FromMilliseconds(50), cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }

                throw new AiProviderUnavailableException("timeout");
            case FakeAiScenario.ServerError:
                throw new AiProviderUnavailableException("http_500: internal error");
            case FakeAiScenario.Valid or FakeAiScenario.HallucinatedNumbers:
                return new AiCompletion(Answering(request, Fixture(scenario)), 812, 640, TimeSpan.FromMilliseconds(1400), ModelName, Digest, "stop");
            default:
                return new AiCompletion(Fixture(scenario), 812, 640, TimeSpan.FromMilliseconds(1400), ModelName, Digest, "stop");
        }
    }

    /// <summary>
    /// Echoes the symbol and data time the prompt asks for (as a real model would), so the fixture stays valid
    /// whatever the clock says. Everything else is the fixture verbatim.
    /// </summary>
    private static string Answering(AiCompletionRequest request, string fixture)
    {
        var prompt = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        var asked = Regex.Match(prompt, "symbol \"(?<symbol>[^\"]+)\" and dataTimestamp \"(?<at>[^\"]+)\"");
        if (!asked.Success)
        {
            return fixture;
        }

        var json = JsonNode.Parse(fixture)!.AsObject();
        json["symbol"] = asked.Groups["symbol"].Value;
        json["dataTimestamp"] = asked.Groups["at"].Value;
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The raw model output for a scenario (embedded <c>Scenarios/*.txt</c>).</summary>
    public static string Fixture(FakeAiScenario scenario)
    {
        var name = scenario switch
        {
            FakeAiScenario.Valid => "valid",
            FakeAiScenario.InvalidJson => "invalid_json",
            FakeAiScenario.SchemaError => "schema_error",
            FakeAiScenario.HallucinatedNumbers => "hallucinated_numbers",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), "This scenario has no text output."),
        };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Astryx.Ai.Fake.{name}.txt")
            ?? throw new InvalidOperationException($"Fake AI fixture '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
