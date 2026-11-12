using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Discovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScannerAndSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scanner_runs",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    universe_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    symbols_total = table.Column<int>(type: "integer", nullable: false),
                    symbols_evaluated = table.Column<int>(type: "integer", nullable: false),
                    symbols_skipped = table.Column<int>(type: "integer", nullable: false),
                    data_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quotes_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bars_through = table.Column<DateOnly>(type: "date", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scanner_runs", x => x.id);
                    table.CheckConstraint("ck_scanner_runs_status", "status IN ('running', 'completed', 'completed_with_warnings', 'failed')");
                    table.CheckConstraint("ck_scanner_runs_trigger", "trigger IN ('schedule', 'manual', 'eod')");
                });

            migrationBuilder.CreateTable(
                name: "universe_members",
                schema: "discovery",
                columns: table => new
                {
                    universe_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_universe_members", x => new { x.universe_code, x.instrument_id });
                });

            migrationBuilder.CreateTable(
                name: "signals",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scanner_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    strategy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    signal_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    horizon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    score_breakdown = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    order_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stop_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signals", x => x.id);
                    table.CheckConstraint("ck_signals_horizon", "horizon IN ('intraday', 'swing', 'position')");
                    table.CheckConstraint("ck_signals_levels", "stop_price < entry_price AND entry_price < target_price");
                    table.CheckConstraint("ck_signals_order_type", "order_type IN ('buy_stop', 'limit', 'market')");
                    table.CheckConstraint("ck_signals_score", "score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_signals_status", "status IN ('active', 'superseded', 'expired', 'invalidated')");
                    table.CheckConstraint("ck_signals_type", "signal_type IN ('momentum', 'breakout', 'pullback', 'unusual_volume', 'earnings')");
                    table.ForeignKey(
                        name: "fk_signals_scanner_runs_scanner_run_id",
                        column: x => x.scanner_run_id,
                        principalSchema: "discovery",
                        principalTable: "scanner_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scanner_runs_started_at",
                schema: "discovery",
                table: "scanner_runs",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_signals_active_by_score",
                schema: "discovery",
                table: "signals",
                columns: new[] { "status", "score" },
                descending: new[] { false, true },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_signals_instrument_id_evaluated_at",
                schema: "discovery",
                table: "signals",
                columns: new[] { "instrument_id", "evaluated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_signals_scanner_run_id_instrument_id_strategy_version_id",
                schema: "discovery",
                table: "signals",
                columns: new[] { "scanner_run_id", "instrument_id", "strategy_version_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signals",
                schema: "discovery");

            migrationBuilder.DropTable(
                name: "universe_members",
                schema: "discovery");

            migrationBuilder.DropTable(
                name: "scanner_runs",
                schema: "discovery");
        }
    }
}
