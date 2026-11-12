namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.provider_sync_state</c>: freshness and rate-budget bookkeeping per provider dataset (§16.3, §20.5).</summary>
public sealed class ProviderSyncState
{
    public required string Provider { get; set; }

    /// <summary>e.g. <c>quotes</c>, <c>eod_bars</c>, <c>news</c>.</summary>
    public required string Dataset { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastErrorCode { get; set; }

    public int ConsecutiveFailures { get; set; }

    public int RequestsToday { get; set; }
}
