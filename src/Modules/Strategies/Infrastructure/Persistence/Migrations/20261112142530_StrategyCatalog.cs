using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Strategies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrategyCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "strategy");

            migrationBuilder.CreateTable(
                name: "backtest_runs",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    oos_start = table.Column<DateOnly>(type: "date", nullable: false),
                    initial_capital = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    assumptions = table.Column<string>(type: "jsonb", nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false),
                    equity_curve = table.Column<string>(type: "jsonb", nullable: false),
                    drawdown_curve = table.Column<string>(type: "jsonb", nullable: false),
                    monthly_returns = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_runs", x => x.id);
                    table.CheckConstraint("ck_backtest_runs_status", "status IN ('pending', 'running', 'completed', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "backtest_trades",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    pnl = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    r_multiple = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    exit_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_trades", x => x.id);
                    table.CheckConstraint("ck_backtest_trades_segment", "segment IN ('is', 'oos')");
                    table.ForeignKey(
                        name: "fk_backtest_trades_backtest_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "strategy",
                        principalTable: "backtest_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategies",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    rules = table.Column<string>(type: "jsonb", nullable: false),
                    param_schema = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    live_since = table.Column<DateOnly>(type: "date", nullable: true),
                    illustrative_live = table.Column<string>(type: "jsonb", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategies", x => x.id);
                    table.CheckConstraint("ck_strategies_status", "status IN ('active', 'active_guarded', 'paused', 'retired')");
                });

            migrationBuilder.CreateTable(
                name: "strategy_versions",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    code_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_versions", x => x.id);
                    table.CheckConstraint("ck_strategy_versions_state", "state IN ('draft', 'published', 'archived')");
                    table.ForeignKey(
                        name: "fk_strategy_versions_strategies_strategy_id",
                        column: x => x.strategy_id,
                        principalSchema: "strategy",
                        principalTable: "strategies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backtest_runs_strategy_id_run_number",
                schema: "strategy",
                table: "backtest_runs",
                columns: new[] { "strategy_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backtest_runs_strategy_version_id",
                schema: "strategy",
                table: "backtest_runs",
                column: "strategy_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_trades_run_id",
                schema: "strategy",
                table: "backtest_trades",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_strategies_code",
                schema: "strategy",
                table: "strategies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strategies_published_version_id",
                schema: "strategy",
                table: "strategies",
                column: "published_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_strategy_versions_strategy_id_version_number",
                schema: "strategy",
                table: "strategy_versions",
                columns: new[] { "strategy_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_backtest_runs_strategies_strategy_id",
                schema: "strategy",
                table: "backtest_runs",
                column: "strategy_id",
                principalSchema: "strategy",
                principalTable: "strategies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_backtest_runs_strategy_versions_strategy_version_id",
                schema: "strategy",
                table: "backtest_runs",
                column: "strategy_version_id",
                principalSchema: "strategy",
                principalTable: "strategy_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_strategies_versions_published_version_id",
                schema: "strategy",
                table: "strategies",
                column: "published_version_id",
                principalSchema: "strategy",
                principalTable: "strategy_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Published (and archived) versions are immutable: params can't change once a version is out (§10.4).
            migrationBuilder.Sql("""
                CREATE FUNCTION strategy.reject_published_version_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.state IN ('published', 'archived') AND NEW.params IS DISTINCT FROM OLD.params THEN
                        RAISE EXCEPTION 'strategy version % is % and its params are immutable', OLD.id, OLD.state USING ERRCODE = 'check_violation';
                    END IF;
                    IF OLD.state IN ('published', 'archived') AND NEW.state = 'draft' THEN
                        RAISE EXCEPTION 'strategy version % cannot return to draft', OLD.id USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END
                $$;
                CREATE TRIGGER trg_strategy_versions_immutable BEFORE UPDATE ON strategy.strategy_versions
                    FOR EACH ROW EXECUTE FUNCTION strategy.reject_published_version_change();
                """);

            // The five design strategies, their versions and one illustrative backtest run each (T-063).
            foreach (var statement in Astryx.Modules.Strategies.Infrastructure.Seed.CatalogSeed.Statements())
            {
                migrationBuilder.Sql(statement);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_strategy_versions_immutable ON strategy.strategy_versions; DROP FUNCTION IF EXISTS strategy.reject_published_version_change();");

            migrationBuilder.DropForeignKey(
                name: "fk_strategy_versions_strategies_strategy_id",
                schema: "strategy",
                table: "strategy_versions");

            migrationBuilder.DropTable(
                name: "backtest_trades",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "backtest_runs",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "strategies",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "strategy_versions",
                schema: "strategy");
        }
    }
}
