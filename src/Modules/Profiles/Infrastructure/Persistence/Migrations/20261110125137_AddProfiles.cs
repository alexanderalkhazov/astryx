using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Profiles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fills = table.Column<bool>(type: "boolean", nullable: false),
                    signals = table.Column<bool>(type: "boolean", nullable: false),
                    stop_proximity = table.Column<bool>(type: "boolean", nullable: false),
                    strategy_guard = table.Column<bool>(type: "boolean", nullable: false),
                    system = table.Column<bool>(type: "boolean", nullable: false),
                    daily_summary = table.Column<bool>(type: "boolean", nullable: false),
                    push_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "risk_settings",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_risk_per_trade_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    max_position_weight_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    max_sector_exposure_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    sector_warning_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    daily_loss_halt_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    max_open_positions = table.Column<int>(type: "integer", nullable: false),
                    max_gross_exposure_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    min_reward_risk = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    allow_extended_hours = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_settings", x => x.user_id);
                    table.CheckConstraint("ck_risk_settings_daily_loss", "daily_loss_halt_pct BETWEEN 0.005 AND 0.05");
                    table.CheckConstraint("ck_risk_settings_gross", "max_gross_exposure_pct = 1");
                    table.CheckConstraint("ck_risk_settings_open_positions", "max_open_positions BETWEEN 1 AND 30");
                    table.CheckConstraint("ck_risk_settings_position_weight", "max_position_weight_pct BETWEEN 0.02 AND 0.25");
                    table.CheckConstraint("ck_risk_settings_reward_risk", "min_reward_risk BETWEEN 1.0 AND 5.0");
                    table.CheckConstraint("ck_risk_settings_risk_per_trade", "max_risk_per_trade_pct BETWEEN 0.001 AND 0.02");
                    table.CheckConstraint("ck_risk_settings_sector", "max_sector_exposure_pct BETWEEN 0.10 AND 0.60 AND sector_warning_pct < max_sector_exposure_pct");
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trading_mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    biometric_lock = table.Column<bool>(type: "boolean", nullable: false),
                    scanner_universe = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scanner_min_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    scanner_min_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    scanner_min_avg_dollar_volume = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.user_id);
                    table.CheckConstraint("ck_user_profiles_trading_mode", "trading_mode = 'paper'");
                });

            // Cross-schema FK to identity.users (owned by the Identity module, migrated first): account erasure cascades.
            migrationBuilder.Sql("""
                ALTER TABLE identity.user_profiles ADD CONSTRAINT fk_user_profiles_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                ALTER TABLE identity.risk_settings ADD CONSTRAINT fk_risk_settings_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                ALTER TABLE identity.notification_preferences ADD CONSTRAINT fk_notification_preferences_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "risk_settings",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "identity");
        }
    }
}
