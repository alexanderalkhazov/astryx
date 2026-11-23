using System.Globalization;
using Astryx.AiEval;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Ai.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// dotnet run --project tools/Astryx.AiEval -- --model qwen3.5:0.8b --prompt stock_analysis@2026-11-09.1 [--limit N]
// Options: --base-url http://localhost:11434 · --provider ollama|fake · --cases <dir> · --out <dir> · --timeout <s> · --min-rr 1.5 · --raw <dir> (keep each model answer)
var options = ParseArgs(args);
var root = FindRepoRoot();
var model = options.GetValueOrDefault("model", "qwen3.5:0.8b");
var (promptId, promptVersion) = SplitPrompt(options.GetValueOrDefault("prompt", "stock_analysis@2026-11-09.1"));
var casesDir = options.GetValueOrDefault("cases", Path.Combine(root, "tests/fixtures/ai/golden"));
var outDir = options.GetValueOrDefault("out", Path.Combine(root, "artifacts/ai-eval"));
var providerName = options.GetValueOrDefault("provider", "ollama");
var timeout = int.Parse(options.GetValueOrDefault("timeout", "90"), CultureInfo.InvariantCulture);
var minRr = decimal.Parse(options.GetValueOrDefault("min-rr", "1.5"), CultureInfo.InvariantCulture);

var promptOptions = new PromptOptions();
promptOptions.Active[promptId] = promptVersion;
var prompt = new PromptRegistry(Options.Create(promptOptions)).Get(promptId, promptVersion);
var cases = GoldenCase.LoadAll(casesDir);
if (options.TryGetValue("limit", out var limit))
{
    cases = cases.Take(int.Parse(limit, CultureInfo.InvariantCulture)).ToList();
}

IAiProvider provider = providerName switch
{
    "fake" => new FakeAiProvider(),
    "ollama" => CreateOllama(options.GetValueOrDefault("base-url", "http://localhost:11434"), model, timeout),
    _ => throw new ArgumentException($"Unknown provider '{providerName}'."),
};

var runner = new EvalRunner(provider, prompt, new AiGenerationOptions { TimeoutSeconds = timeout }, minRr, options.GetValueOrDefault("raw"));
var startedAt = DateTimeOffset.UtcNow;
var results = new List<CaseResult>();
foreach (var golden in cases)
{
    var result = await runner.RunAsync(golden, CancellationToken.None);
    results.Add(result);
    Console.WriteLine($"{golden.Id} {result.Outcome,-22} calls={result.Calls} {result.LatencyMs / 1000m:0.0}s {result.FirstIssue}");
}

var report = EvalReport.Build(model, $"{promptId}@{promptVersion}", providerName, startedAt, results);
Directory.CreateDirectory(outDir);
var stem = Path.Combine(outDir, $"{model.Replace(':', '_')}_{promptId}@{promptVersion}_{startedAt:yyyyMMdd-HHmm}");
await File.WriteAllTextAsync(stem + ".json", report.ToJson());
await File.WriteAllTextAsync(stem + ".md", report.ToMarkdown());
Console.WriteLine($"Gate {(report.GatePassed ? "PASS" : "FAIL")} · schema {report.SchemaValidAfterRepair} · grounding {report.GroundingViolations} · injection {report.InjectionObeyed} · p95 {report.LatencyP95Ms / 1000m:0.0}s");
Console.WriteLine($"Wrote {stem}.md and .json");
return 0;

static OllamaAiProvider CreateOllama(string baseUrl, string model, int timeoutSeconds)
{
    var settings = new OllamaOptions { BaseUrl = baseUrl, Model = model, TimeoutSeconds = timeoutSeconds };
    var capabilities = new OllamaCapabilities();
    var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds + 5) };
    var provider = new OllamaAiProvider(http, Options.Create(settings), capabilities, new CircuitBreaker(TimeProvider.System), new OllamaModelInfoCache(), TimeProvider.System, NullLogger<OllamaAiProvider>.Instance);
    capabilities.SupportsStructuredOutput = provider.ProbeStructuredOutputAsync(CancellationToken.None).GetAwaiter().GetResult() ?? false;
    Console.WriteLine($"{model} at {baseUrl}: structured output {(capabilities.SupportsStructuredOutput == true ? "supported" : "not supported")}");
    return provider;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length - 1; i += 2)
    {
        result[args[i].TrimStart('-')] = args[i + 1];
    }

    return result;
}

static (string Id, string Version) SplitPrompt(string value)
{
    var parts = value.Split('@', 2);
    return parts.Length == 2 ? (parts[0], parts[1]) : throw new ArgumentException("--prompt must be <id>@<version>.");
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Astryx.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
