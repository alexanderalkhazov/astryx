using Astryx.Modules.Ai.Application.Providers;

namespace Astryx.Modules.Ai.Infrastructure.Ollama;

/// <summary>
/// Fails fast after repeated provider failures (SYSTEM_DESIGN.md §12.3): <see cref="FailureThreshold"/> failures within
/// <see cref="Window"/> open the circuit for <see cref="OpenFor"/>. After that one trial call goes through; a failure
/// re-opens it at once, a success closes it.
/// </summary>
public sealed class CircuitBreaker(TimeProvider timeProvider)
{
    public const int FailureThreshold = 5;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan OpenFor = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private DateTimeOffset? _openUntil;
    private bool _halfOpen;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _openUntil is { } until && timeProvider.GetUtcNow() < until;
            }
        }
    }

    /// <exception cref="AiProviderUnavailableException">The circuit is open.</exception>
    public void EnsureClosed()
    {
        lock (_gate)
        {
            if (_openUntil is not { } until)
            {
                return;
            }

            if (timeProvider.GetUtcNow() < until)
            {
                throw new AiProviderUnavailableException("circuit_open");
            }

            _openUntil = null;
            _halfOpen = true; // let one call through
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures.Clear();
            _halfOpen = false;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            if (_halfOpen)
            {
                _halfOpen = false;
                _openUntil = now + OpenFor;
                return;
            }

            _failures.Enqueue(now);
            while (_failures.Count > 0 && now - _failures.Peek() > Window)
            {
                _failures.Dequeue();
            }

            if (_failures.Count >= FailureThreshold)
            {
                _failures.Clear();
                _openUntil = now + OpenFor;
            }
        }
    }
}
