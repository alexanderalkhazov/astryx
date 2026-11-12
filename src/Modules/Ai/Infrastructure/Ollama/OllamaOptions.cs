namespace Astryx.Modules.Ai.Infrastructure.Ollama;

/// <summary><c>Ai:Ollama</c> (SYSTEM_DESIGN.md §12.3).</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ai:Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen3.5:0.8b";

    /// <summary>Total time for one completion; there is no automatic HTTP retry (jobs own retries, §12.8).</summary>
    public int TimeoutSeconds { get; set; } = 90;

    public string KeepAlive { get; set; } = "10m";

    /// <summary>Forces structured-output support on or off; null means probe at startup.</summary>
    public bool? SupportsStructuredOutput { get; set; }
}

/// <summary>What the startup probe learned about the model. Null until known.</summary>
public sealed class OllamaCapabilities
{
    private int _structured = -1; // -1 unknown, 0 no, 1 yes

    public bool? SupportsStructuredOutput
    {
        get => _structured switch { 1 => true, 0 => false, _ => null };
        set => Interlocked.Exchange(ref _structured, value switch { true => 1, false => 0, null => -1 });
    }
}

/// <summary>Model info from <c>/api/tags</c> and <c>/api/show</c>, cached for ten minutes (§12.3).</summary>
public sealed class OllamaModelInfoCache
{
    private readonly Lock _gate = new();
    private (Application.Providers.AiModelInfo Info, DateTimeOffset Expires)? _entry;

    public Application.Providers.AiModelInfo? Get(string model, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _entry is { } e && e.Info.Name == model && e.Expires > now ? e.Info : null;
        }
    }

    public void Set(Application.Providers.AiModelInfo info, DateTimeOffset expires)
    {
        lock (_gate)
        {
            _entry = (info, expires);
        }
    }
}
