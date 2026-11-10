using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "portfolio");

            migrationBuilder.CreateTable(
                name: "portfolios",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    broker_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    starting_cash = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    cash_balance = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    reserved_cash = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    realized_pnl = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    start_of_day_equity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    start_of_day_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reset_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolios", x => x.id);
                    table.CheckConstraint("ck_portfolios_cash", "cash_balance >= 0 AND reserved_cash >= 0 AND reserved_cash <= cash_balance");
                    table.CheckConstraint("ck_portfolios_mode", "mode IN ('simulated', 'etoro_demo')");
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    fill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_type", "type IN ('initial_deposit', 'buy', 'sell', 'fee', 'reset_close', 'reset_deposit', 'adjustment')");
                    table.ForeignKey(
                        name: "fk_transactions_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_portfolios_user_id_mode",
                schema: "portfolio",
                table: "portfolios",
                columns: new[] { "user_id", "mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_idempotency_ref",
                schema: "portfolio",
                table: "transactions",
                column: "idempotency_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_portfolio_id_occurred_at",
                schema: "portfolio",
                table: "transactions",
                columns: new[] { "portfolio_id", "occurred_at" },
                descending: new[] { false, true });

            // Cross-schema FK to identity.users (owned by the Identity module, migrated first): account erasure cascades.
            migrationBuilder.Sql("""
                ALTER TABLE portfolio.portfolios ADD CONSTRAINT fk_portfolios_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolios",
                schema: "portfolio");
        }
    }
}
