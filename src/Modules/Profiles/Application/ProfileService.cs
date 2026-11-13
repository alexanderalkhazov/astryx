using Astryx.Contracts.Audit;
using Astryx.Contracts.Events.Profiles;
using Astryx.Modules.Profiles.Domain;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Application;

public sealed record ProfileUpdate(string? DisplayName, string? Timezone, string? Locale, bool? BiometricLock);

public sealed record ScannerUpdate(string Universe, decimal MinPrice, decimal MinAvgDollarVolume, decimal? MinScore);

public sealed record RiskSettingsUpdate(
    decimal MaxRiskPerTradePct,
    decimal MaxPositionWeightPct,
    decimal MaxSectorExposurePct,
    decimal SectorWarningPct,
    decimal DailyLossHaltPct,
    int MaxOpenPositions,
    decimal MinRewardRisk);

public sealed record NotificationUpdate(bool Fills, bool Signals, bool StopProximity, bool StrategyGuard, bool System, bool DailySummary, bool PushEnabled);

/// <summary>Profile, risk limits and notification preferences of the signed-in user (SYSTEM_DESIGN.md §7.2, §11.3).</summary>
public sealed class ProfileService(ProfilesDbContext db, ProfileProvisioner provisioner, TimeProvider timeProvider)
{
    public static readonly Error PreconditionRequired = new("precondition_required", "Send the If-Match header with the ETag you last read.");
    public static readonly Error PreconditionFailed = new("precondition_failed", "These settings changed since you loaded them. Reload and try again.");
    public static readonly Error LiveTradingDisabled = new("live_trading_disabled", "Live trading isn't available yet. Your account stays in paper mode.");

    public async Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        return await db.Profiles.AsNoTracking().SingleAsync(p => p.UserId == userId, cancellationToken);
    }

    public async Task<UserProfile> UpdateProfileAsync(Guid userId, ProfileUpdate update, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        var profile = await db.Profiles.SingleAsync(p => p.UserId == userId, cancellationToken);
        var before = new { profile.DisplayName, profile.Timezone, profile.Locale, profile.BiometricLock };
        if (update.DisplayName is not null)
        {
            profile.DisplayName = string.IsNullOrWhiteSpace(update.DisplayName) ? null : update.DisplayName.Trim();
        }

        profile.Timezone = update.Timezone ?? profile.Timezone;
        profile.Locale = update.Locale ?? profile.Locale;
        profile.BiometricLock = update.BiometricLock ?? profile.BiometricLock;
        profile.UpdatedAt = timeProvider.GetUtcNow();
        db.Audit(new AuditEntry("profile.updated", AuditActor.User, userId, "user_profile", userId.ToString(), before,
            new { profile.DisplayName, profile.Timezone, profile.Locale, profile.BiometricLock }));
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    /// <summary>S3: saved per user and audited. The shared scanner does not read them yet (§8.4.11).</summary>
    public async Task<UserProfile> UpdateScannerAsync(Guid userId, ScannerUpdate update, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        var profile = await db.Profiles.SingleAsync(p => p.UserId == userId, cancellationToken);
        var before = new { profile.ScannerUniverse, profile.ScannerMinPrice, profile.ScannerMinAvgDollarVolume, profile.ScannerMinScore };
        profile.ScannerUniverse = update.Universe;
        profile.ScannerMinPrice = update.MinPrice;
        profile.ScannerMinAvgDollarVolume = update.MinAvgDollarVolume;
        profile.ScannerMinScore = update.MinScore;
        profile.UpdatedAt = timeProvider.GetUtcNow();
        db.Audit(new AuditEntry("scanner_settings.updated", AuditActor.User, userId, "user_profile", userId.ToString(), before,
            new { profile.ScannerUniverse, profile.ScannerMinPrice, profile.ScannerMinAvgDollarVolume, profile.ScannerMinScore }));
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<RiskSettings> GetRiskSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        return await db.RiskSettings.AsNoTracking().SingleAsync(r => r.UserId == userId, cancellationToken);
    }

    /// <summary>Replaces the limits if <paramref name="expectedVersion"/> is current; bumps the version and publishes the change.</summary>
    public async Task<Result<RiskSettings>> UpdateRiskSettingsAsync(Guid userId, int? expectedVersion, RiskSettingsUpdate update, CancellationToken cancellationToken)
    {
        if (expectedVersion is null)
        {
            return PreconditionRequired;
        }

        await provisioner.EnsureAsync(userId, null, cancellationToken);
        var settings = await db.RiskSettings.SingleAsync(r => r.UserId == userId, cancellationToken);
        if (settings.Version != expectedVersion)
        {
            return PreconditionFailed;
        }

        var before = Snapshot(settings);
        settings.MaxRiskPerTradePct = update.MaxRiskPerTradePct;
        settings.MaxPositionWeightPct = update.MaxPositionWeightPct;
        settings.MaxSectorExposurePct = update.MaxSectorExposurePct;
        settings.SectorWarningPct = update.SectorWarningPct;
        settings.DailyLossHaltPct = update.DailyLossHaltPct;
        settings.MaxOpenPositions = update.MaxOpenPositions;
        settings.MinRewardRisk = update.MinRewardRisk;
        settings.Version++;
        settings.UpdatedAt = timeProvider.GetUtcNow();
        db.Publish(new RiskSettingsChanged(userId, settings.Version));
        db.Audit(new AuditEntry("risk_settings.updated", AuditActor.User, userId, "risk_settings", userId.ToString(), before, Snapshot(settings)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed; // another request bumped the version between our read and write
        }

        return settings;
    }

    public async Task<NotificationPreferences> GetNotificationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        return await db.NotificationPreferences.AsNoTracking().SingleAsync(n => n.UserId == userId, cancellationToken);
    }

    public async Task<NotificationPreferences> UpdateNotificationsAsync(Guid userId, NotificationUpdate update, CancellationToken cancellationToken)
    {
        await provisioner.EnsureAsync(userId, null, cancellationToken);
        var prefs = await db.NotificationPreferences.SingleAsync(n => n.UserId == userId, cancellationToken);
        prefs.Fills = update.Fills;
        prefs.Signals = update.Signals;
        prefs.StopProximity = update.StopProximity;
        prefs.StrategyGuard = update.StrategyGuard;
        prefs.System = update.System;
        prefs.DailySummary = update.DailySummary;
        prefs.PushEnabled = update.PushEnabled;
        prefs.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return prefs;
    }

    /// <summary>Paper is the only mode; a request for live is refused and recorded (SYSTEM_DESIGN.md §17.9).</summary>
    public async Task<Result<string>> SetTradingModeAsync(Guid userId, string mode, CancellationToken cancellationToken)
    {
        if (mode == "paper")
        {
            return "paper";
        }

        db.Audit(new AuditEntry("trading_mode.live_attempt", AuditActor.User, userId, "user_profile", userId.ToString(), Metadata: new { requested = mode }));
        await db.SaveChangesAsync(cancellationToken);
        return LiveTradingDisabled;
    }

    private static object Snapshot(RiskSettings s) => new
    {
        s.MaxRiskPerTradePct, s.MaxPositionWeightPct, s.MaxSectorExposurePct, s.SectorWarningPct, s.DailyLossHaltPct, s.MaxOpenPositions, s.MinRewardRisk, s.Version,
    };
}
