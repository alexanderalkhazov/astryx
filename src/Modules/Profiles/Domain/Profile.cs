namespace Astryx.Modules.Profiles.Domain;

/// <summary>Row in <c>identity.user_profiles</c>.</summary>
public sealed class UserProfile
{
    public Guid UserId { get; set; }

    public string? DisplayName { get; set; }

    public string Timezone { get; set; } = "America/New_York";

    public string Locale { get; set; } = "en-US";

    public string TradingMode { get; set; } = "paper";

    public bool BiometricLock { get; set; }

    public string ScannerUniverse { get; set; } = "us_large_cap";

    public decimal? ScannerMinScore { get; set; }

    /// <summary>Design Settings: "Min price · avg $ volume — $10 · $20M".</summary>
    public decimal ScannerMinPrice { get; set; } = 10m;

    public decimal ScannerMinAvgDollarVolume { get; set; } = 20_000_000m;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Row in <c>identity.risk_settings</c>; defaults are the design's limits (SYSTEM_DESIGN.md §14.3).</summary>
public sealed class RiskSettings
{
    public Guid UserId { get; set; }

    public decimal MaxRiskPerTradePct { get; set; } = 0.0075m;

    public decimal MaxPositionWeightPct { get; set; } = 0.12m;

    public decimal MaxSectorExposurePct { get; set; } = 0.35m;

    public decimal SectorWarningPct { get; set; } = 0.30m;

    public decimal DailyLossHaltPct { get; set; } = 0.02m;

    public int MaxOpenPositions { get; set; } = 12;

    public decimal MaxGrossExposurePct { get; set; } = 1m;

    public decimal MinRewardRisk { get; set; } = 1.5m;

    public bool AllowExtendedHours { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Row in <c>identity.notification_preferences</c>; categories mirror the design's Settings.</summary>
public sealed class NotificationPreferences
{
    public Guid UserId { get; set; }

    public bool Fills { get; set; } = true;

    public bool Signals { get; set; } = true;

    public bool StopProximity { get; set; } = true;

    public bool StrategyGuard { get; set; } = true;

    public bool System { get; set; } = true;

    public bool DailySummary { get; set; }

    public bool PushEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
