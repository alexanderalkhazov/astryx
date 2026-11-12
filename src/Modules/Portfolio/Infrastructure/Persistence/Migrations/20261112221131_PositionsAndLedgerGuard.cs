using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PositionsAndLedgerGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "seq",
                schema: "portfolio",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    avg_entry_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_basis = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    stop_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    target_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    entry_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    broker_position_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    exit_reason = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    realized_pnl = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    fees_paid = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.CheckConstraint("ck_positions_closed", "(status = 'open' AND closed_at IS NULL) OR (status = 'closed' AND closed_at IS NOT NULL AND exit_reason IS NOT NULL)");
                    table.CheckConstraint("ck_positions_exit_reason", "exit_reason IS NULL OR exit_reason IN ('stop', 'target', 'manual', 'reset')");
                    table.CheckConstraint("ck_positions_quantity", "quantity >= 0");
                    table.CheckConstraint("ck_positions_status", "status IN ('open', 'closed')");
                    table.ForeignKey(
                        name: "fk_positions_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                schema: "portfolio",
                columns: table => new
                {
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    snapshot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    equity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    cash = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    invested = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    realized_pnl_cum = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    benchmark_close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    positions_count = table.Column<int>(type: "integer", nullable: false),
                    stale = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshots", x => new { x.portfolio_id, x.kind, x.snapshot_at });
                    table.CheckConstraint("ck_snapshots_kind", "kind IN ('eod', 'hourly')");
                    table.ForeignKey(
                        name: "fk_snapshots_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_portfolio_id_seq",
                schema: "portfolio",
                table: "transactions",
                columns: new[] { "portfolio_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_closed",
                schema: "portfolio",
                table: "positions",
                columns: new[] { "portfolio_id", "closed_at" },
                descending: new[] { false, true },
                filter: "status = 'closed'");

            migrationBuilder.CreateIndex(
                name: "ix_positions_portfolio_id_status",
                schema: "portfolio",
                table: "positions",
                columns: new[] { "portfolio_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_positions_one_open",
                schema: "portfolio",
                table: "positions",
                columns: new[] { "portfolio_id", "instrument_id" },
                unique: true,
                filter: "status = 'open'");

            // The ledger is append-only (§10.4, §20.8). A REVOKE can't bind the table owner, so a trigger enforces it for every role.
            migrationBuilder.Sql("""
                CREATE FUNCTION portfolio.reject_ledger_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'portfolio.transactions is append-only (% refused)', TG_OP USING ERRCODE = 'insufficient_privilege';
                END $$;
                CREATE TRIGGER trg_transactions_append_only BEFORE UPDATE OR DELETE ON portfolio.transactions
                    FOR EACH ROW EXECUTE FUNCTION portfolio.reject_ledger_change();
                CREATE TRIGGER trg_transactions_no_truncate BEFORE TRUNCATE ON portfolio.transactions
                    FOR EACH STATEMENT EXECUTE FUNCTION portfolio.reject_ledger_change();
                REVOKE UPDATE, DELETE, TRUNCATE ON portfolio.transactions FROM PUBLIC;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_transactions_no_truncate ON portfolio.transactions; DROP TRIGGER IF EXISTS trg_transactions_append_only ON portfolio.transactions; DROP FUNCTION IF EXISTS portfolio.reject_ledger_change();");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "snapshots",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_transactions_portfolio_id_seq",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "seq",
                schema: "portfolio",
                table: "transactions");
        }
    }
}
