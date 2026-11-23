using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Invest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvestInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invest");

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "invest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cash = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    contributed = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    contribution_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    contribution_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    next_contribution_on = table.Column<DateOnly>(type: "date", nullable: true),
                    goal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    goal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_cash", "cash >= 0");
                    table.CheckConstraint("ck_plans_frequency", "contribution_frequency IN ('none', 'weekly', 'monthly')");
                    table.CheckConstraint("ck_plans_profile", "profile IN ('cautious', 'balanced', 'growth')");
                });

            migrationBuilder.CreateTable(
                name: "holdings",
                schema: "invest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_basis = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdings", x => x.id);
                    table.CheckConstraint("ck_holdings_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_holdings_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "invest",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "invest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_kind", "kind IN ('deposit', 'buy', 'sell')");
                    table.ForeignKey(
                        name: "fk_transactions_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "invest",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holdings_plan_id_symbol",
                schema: "invest",
                table: "holdings",
                columns: new[] { "plan_id", "symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_next_contribution_on",
                schema: "invest",
                table: "plans",
                column: "next_contribution_on",
                filter: "contribution_frequency <> 'none'");

            migrationBuilder.CreateIndex(
                name: "ix_plans_user_id",
                schema: "invest",
                table: "plans",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_plan_id_occurred_at",
                schema: "invest",
                table: "transactions",
                columns: new[] { "plan_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holdings",
                schema: "invest");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "invest");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "invest");
        }
    }
}
