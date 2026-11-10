using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Profiles.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Infrastructure.Persistence;

/// <summary>Profiles module data in schema <c>identity</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<UserProfile> Profiles => Set<UserProfile>();

    public DbSet<RiskSettings> RiskSettings => Set<RiskSettings>();

    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles", t => t.HasCheckConstraint("ck_user_profiles_trading_mode", "trading_mode = 'paper'"));
            entity.HasKey(p => p.UserId);
            entity.Property(p => p.DisplayName).HasMaxLength(80);
            entity.Property(p => p.Timezone).HasMaxLength(64);
            entity.Property(p => p.Locale).HasMaxLength(16);
            entity.Property(p => p.TradingMode).HasMaxLength(8);
            entity.Property(p => p.ScannerUniverse).HasMaxLength(64);
            entity.Property(p => p.ScannerMinScore).HasPrecision(5, 2);
            entity.Property(p => p.ScannerMinPrice).HasPrecision(18, 6);
            entity.Property(p => p.ScannerMinAvgDollarVolume).HasPrecision(20, 2);
        });

        modelBuilder.Entity<RiskSettings>(entity =>
        {
            entity.ToTable("risk_settings", t =>
            {
                // Bounds from SYSTEM_DESIGN.md §14.3; the API validates the same ranges first.
                t.HasCheckConstraint("ck_risk_settings_risk_per_trade", "max_risk_per_trade_pct BETWEEN 0.001 AND 0.02");
                t.HasCheckConstraint("ck_risk_settings_position_weight", "max_position_weight_pct BETWEEN 0.02 AND 0.25");
                t.HasCheckConstraint("ck_risk_settings_sector", "max_sector_exposure_pct BETWEEN 0.10 AND 0.60 AND sector_warning_pct < max_sector_exposure_pct");
                t.HasCheckConstraint("ck_risk_settings_daily_loss", "daily_loss_halt_pct BETWEEN 0.005 AND 0.05");
                t.HasCheckConstraint("ck_risk_settings_open_positions", "max_open_positions BETWEEN 1 AND 30");
                t.HasCheckConstraint("ck_risk_settings_reward_risk", "min_reward_risk BETWEEN 1.0 AND 5.0");
                t.HasCheckConstraint("ck_risk_settings_gross", "max_gross_exposure_pct = 1");
            });
            entity.HasKey(r => r.UserId);
            foreach (var pct in new[] { nameof(Domain.RiskSettings.MaxRiskPerTradePct), nameof(Domain.RiskSettings.MaxPositionWeightPct), nameof(Domain.RiskSettings.MaxSectorExposurePct), nameof(Domain.RiskSettings.SectorWarningPct), nameof(Domain.RiskSettings.DailyLossHaltPct), nameof(Domain.RiskSettings.MaxGrossExposurePct) })
            {
                entity.Property(pct).HasPrecision(9, 6);
            }

            entity.Property(r => r.MinRewardRisk).HasPrecision(8, 4);
            entity.Property(r => r.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<NotificationPreferences>(entity =>
        {
            entity.ToTable("notification_preferences");
            entity.HasKey(n => n.UserId);
        });
    }
}
