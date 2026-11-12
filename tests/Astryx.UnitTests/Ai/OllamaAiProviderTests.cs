using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Ai.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Astryx.UnitTests.Ai;

/// <summary>Ollama provider, circuit breaker and fake provider (SYSTEM_DESIGN.md §12.3, TASKS.md T-070).</summary>
public class OllamaAiProviderTests
{
    private static readonly JsonDocument Schema = JsonDocument.Parse("""{"type":"object","required":["symbol"],"properties":{"symbol":{"type":"string"}}}""");

    /// <summary>Answers Ollama routes from a function and records every request body.</summary>
    private sealed class StubOllama(Func<string, JsonNode?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(string Path, JsonNode? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Calls.Add((path, body));
            return answer(path, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Tags = """{"models":[{"name":"qwen3.5:0.8b","digest":"d9c89605f39a","details":{"parameter_size":"0.8B","quantization_level":"Q4_K_M"}}]}""";
    private const string Show = """{"model_info":{"qwen35.context_length":32768}}""";
    private const string Chat = """{"model":"qwen3.5:0.8b","message":{"role":"assistant","content":"{\"symbol\":\"NVDA\"}"},"done":true,"done_reason":"stop","prompt_eval_count":120,"eval_count":14}""";

    private static (OllamaAiProvider Provider, StubOllama Stub, OllamaCapabilities Capabilities, CircuitBreaker Breaker, FakeTimeProvider Clock) Create(
        Func<string, JsonNode?, HttpResponseMessage>? chat = null, bool? structured = false)
    {
        var stub = new StubOllama((path, body) => path switch
        {
            "api/tags" => Json(Tags),
            "api/show" => Json(Show),
            _ => chat?.Invoke(path, body) ?? Json(Chat),
        });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 19, 32, 0, TimeSpan.Zero));
        var capabilities = new OllamaCapabilities { SupportsStructuredOutput = structured };
        var breaker = new CircuitBreaker(clock);
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://ollama.test/") };
        var provider = new OllamaAiProvider(http, Options.Create(new OllamaOptions()), capabilities, breaker, new OllamaModelInfoCache(), clock, NullLogger<OllamaAiProvider>.Instance);
        return (provider, stub, capabilities, breaker, clock);
    }

    private static AiCompletionRequest Request(TimeSpan? timeout = null) =>
        new("qwen3.5:0.8b", [AiMessage.System("You are a careful analyst."), AiMessage.User("Analyse NVDA.")], Schema, 0.2m, 1200, timeout ?? TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Without_structured_output_the_schema_goes_in_the_system_prompt()
    {
        var (provider, stub, _, _, _) = Create(structured: false);

        var completion = await provider.CompleteJsonAsync(Request(), default);

        var body = stub.Calls.Single(c => c.Path == "api/chat").Body!;
        body["format"].ShouldBeNull();
        body["stream"]!.GetValue<bool>().ShouldBeFalse();
        body["think"]!.GetValue<bool>().ShouldBeFalse();
        body["options"]!["num_predict"]!.GetValue<int>().ShouldBe(1200);
        body["options"]!["temperature"]!.GetValue<decimal>().ShouldBe(0.2m);
        body["keep_alive"]!.GetValue<string>().ShouldBe("10m");
        var system = body["messages"]![0]!["content"]!.GetValue<string>();
        system.ShouldStartWith("You are a careful analyst.");
        system.ShouldContain("\"required\":[\"symbol\"]");
        (completion.RawText, completion.PromptTokens, completion.CompletionTokens, completion.FinishReason, completion.ModelDigest)
            .ShouldBe(("{\"symbol\":\"NVDA\"}", 120, 14, "stop", "d9c89605f39a"));
    }

    [Fact]
    public async Task An_output_example_replaces_the_embedded_schema()
    {
        var (provider, stub, _, _, _) = Create(structured: false);

        await provider.CompleteJsonAsync(Request() with { OutputExample = "{\"symbol\": \"<symbol>\"}" }, default);

        var system = stub.Calls.Single(c => c.Path == "api/chat").Body!["messages"]![0]!["content"]!.GetValue<string>();
        system.ShouldContain("in exactly this shape");
        system.ShouldContain("{\"symbol\": \"<symbol>\"}");
        system.ShouldNotContain("JSON Schema");
    }

    [Fact]
    public async Task With_structured_output_the_schema_goes_in_format()
    {
        var (provider, stub, _, _, _) = Create(structured: true);

        await provider.CompleteJsonAsync(Request(), default);

        var body = stub.Calls.Single(c => c.Path == "api/chat").Body!;
        body["format"]!["required"]![0]!.GetValue<string>().ShouldBe("symbol");
        body["messages"]![0]!["content"]!.GetValue<string>().ShouldBe("You are a careful analyst.");
    }

    [Fact]
    public async Task A_501_for_format_falls_back_to_the_prompt_and_remembers()
    {
        var (provider, stub, capabilities, _, _) = Create(
            chat: (_, body) => body?["format"] is null ? Json(Chat) : Json("""{"error":"structured output is unavailable"}""", HttpStatusCode.NotImplemented),
            structured: true);

        var completion = await provider.CompleteJsonAsync(Request(), default);

        completion.RawText.ShouldBe("{\"symbol\":\"NVDA\"}");
        capabilities.SupportsStructuredOutput.ShouldBe(false);
        stub.Calls.Count(c => c.Path == "api/chat").ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotImplemented, false)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, null)] // model missing: unknown
    public async Task The_probe_reads_structured_output_support_from_the_status(HttpStatusCode status, bool? expected)
    {
        var (provider, stub, _, _, _) = Create(chat: (_, _) => Json("{}", status), structured: null);

        (await provider.ProbeStructuredOutputAsync(default)).ShouldBe(expected);
        var body = stub.Calls.Single().Body!;
        body["format"].ShouldNotBeNull();
        body["options"]!["num_predict"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task Server_errors_are_unavailable_and_five_open_the_circuit_without_calling_ollama()
    {
        var (provider, stub, _, breaker, clock) = Create(chat: (_, _) => Json("""{"error":"boom"}""", HttpStatusCode.InternalServerError));

        for (var i = 0; i < 5; i++)
        {
            var error = await Should.ThrowAsync<AiProviderUnavailableException>(() => provider.CompleteJsonAsync(Request(), default));
            error.Reason.ShouldStartWith("http_500");
        }

        breaker.IsOpen.ShouldBeTrue();
        var calls = stub.Calls.Count;
        (await Should.ThrowAsync<AiProviderUnavailableException>(() => provider.CompleteJsonAsync(Request(), default))).Reason.ShouldBe("circuit_open");
        stub.Calls.Count.ShouldBe(calls); // failed fast

        clock.Advance(CircuitBreaker.OpenFor + TimeSpan.FromSeconds(1));
        breaker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task A_slow_model_times_out_as_unavailable()
    {
        var (provider, _, _, _, _) = Create(chat: (_, _) =>
        {
            Thread.Sleep(300);
            return Json(Chat);
        });

        (await Should.ThrowAsync<AiProviderUnavailableException>(() => provider.CompleteJsonAsync(Request(TimeSpan.FromMilliseconds(50)), default))).Reason.ShouldBe("timeout");
    }

    [Fact]
    public async Task Model_info_comes_from_tags_and_show_and_is_cached_for_ten_minutes()
    {
        var (provider, stub, _, _, clock) = Create();

        var info = await provider.GetModelInfoAsync(default);
        await provider.GetModelInfoAsync(default);

        info.ShouldBe(new AiModelInfo("qwen3.5:0.8b", "d9c89605f39a", 32768, "0.8B", "Q4_K_M"));
        stub.Calls.Count(c => c.Path == "api/tags").ShouldBe(1);
        clock.Advance(TimeSpan.FromMinutes(11));
        await provider.GetModelInfoAsync(default);
        stub.Calls.Count(c => c.Path == "api/tags").ShouldBe(2);
    }

    [Fact]
    public void Breaker_counts_failures_only_within_the_window_and_a_failed_trial_reopens()
    {
        var clock = new FakeTimeProvider();
        var breaker = new CircuitBreaker(clock);
        for (var i = 0; i < 4; i++)
        {
            breaker.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(61));
        breaker.RecordFailure(); // the first four aged out
        breaker.IsOpen.ShouldBeFalse();

        for (var i = 0; i < 4; i++)
        {
            breaker.RecordFailure();
        }

        breaker.IsOpen.ShouldBeTrue();
        clock.Advance(CircuitBreaker.OpenFor);
        breaker.EnsureClosed(); // half-open trial allowed
        breaker.RecordFailure();
        breaker.IsOpen.ShouldBeTrue();
        clock.Advance(CircuitBreaker.OpenFor);
        breaker.EnsureClosed();
        breaker.RecordSuccess();
        breaker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task The_fake_provider_plays_scripted_scenarios_then_its_default()
    {
        var fake = new FakeAiProvider();
        fake.Enqueue(FakeAiScenario.InvalidJson, FakeAiScenario.ServerError);

        var first = await fake.CompleteJsonAsync(Request(), default);
        Should.Throw<JsonException>(() => JsonDocument.Parse(first.RawText));
        (await Should.ThrowAsync<AiProviderUnavailableException>(() => fake.CompleteJsonAsync(Request(), default))).Reason.ShouldStartWith("http_500");
        var valid = await fake.CompleteJsonAsync(Request(), default);
        JsonDocument.Parse(valid.RawText).RootElement.GetProperty("suggestedLevels").GetProperty("stopLoss").GetString().ShouldBe("178.20");
        (await Should.ThrowAsync<AiProviderUnavailableException>(() => { fake.Enqueue(FakeAiScenario.Timeout); return fake.CompleteJsonAsync(Request(TimeSpan.FromMilliseconds(10)), default); })).Reason.ShouldBe("timeout");
        fake.Requests.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData(FakeAiScenario.SchemaError, "explanation")]
    [InlineData(FakeAiScenario.HallucinatedNumbers, "positionSize")]
    public void Fixture_scenarios_parse_but_carry_their_defect(FakeAiScenario scenario, string field)
    {
        var json = JsonDocument.Parse(FakeAiProvider.Fixture(scenario)).RootElement;
        json.TryGetProperty(field, out _).ShouldBeTrue();
    }
}
