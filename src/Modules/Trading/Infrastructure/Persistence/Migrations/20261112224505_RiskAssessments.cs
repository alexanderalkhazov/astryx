using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Trading.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RiskAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_assessments",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inputs_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    risk_per_trade_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    sized_by = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    max_qty_by_risk = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    max_qty_by_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    max_qty_by_cash = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    quote_last = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    quote_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quote_seq = table.Column<long>(type: "bigint", nullable: false),
                    equity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    cash_available = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    portfolio_version = table.Column<long>(type: "bigint", nullable: false),
                    risk_settings_version = table.Column<int>(type: "integer", nullable: false),
                    proposal_version = table.Column<int>(type: "integer", nullable: false),
                    capital_required = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    max_loss = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    max_loss_incl_costs = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    potential_profit = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    reward_risk = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    est_fees = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    est_slippage = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    impact = table.Column<string>(type: "jsonb", nullable: false),
                    checks = table.Column<string>(type: "jsonb", nullable: false),
                    approvable = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_assessments", x => x.id);
                    table.CheckConstraint("ck_risk_assessments_sized_by", "sized_by IN ('risk', 'weight_cap', 'cash', 'user')");
                    table.ForeignKey(
                        name: "fk_risk_assessments_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalSchema: "trading",
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_risk_assessments_proposal_id_created_at",
                schema: "trading",
                table: "risk_assessments",
                columns: new[] { "proposal_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_assessments",
                schema: "trading");
        }
    }
}
