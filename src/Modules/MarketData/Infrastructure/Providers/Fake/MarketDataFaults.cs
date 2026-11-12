namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

public enum MarketDataFaultMode
{
    Ok,

    /// <summary>Every call throws <c>MarketDataUnavailableException</c>.</summary>
    Down,

    /// <summary>Quotes stop advancing: values and <c>asOf</c> stay at the moment the fault was set.</summary>
    Stale,

    /// <summary>The first symbol of each quote request comes back at ten times its previous close.</summary>
    BadTick,
}

/// <summary>
/// Runtime fault switches for the fake provider, set through <c>/dev/faults</c> (Development only, SYSTEM_DESIGN.md
/// §11.11) or directly in tests.
/// </summary>
public sealed class MarketDataFaults(TimeProvider timeProvider)
{
    private volatile State _state = new(MarketDataFaultMode.Ok, null);

    public MarketDataFaultMode Mode => _state.Mode;

    /// <summary>When <see cref="MarketDataFaultMode.Stale"/> froze the quotes.</summary>
    public DateTimeOffset? FrozenAt => _state.FrozenAt;

    public void Set(MarketDataFaultMode mode) =>
        _state = new State(mode, mode == MarketDataFaultMode.Stale ? timeProvider.GetUtcNow() : null);

    private sealed record State(MarketDataFaultMode Mode, DateTimeOffset? FrozenAt);
}
