using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Paper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlpacaBrokerage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_broker",
                schema: "trading",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_mode",
                schema: "trading",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_broker",
                schema: "trading",
                table: "orders",
                sql: "broker IN ('mock', 'alpaca')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_mode",
                schema: "trading",
                table: "orders",
                sql: "mode IN ('simulated', 'alpaca_paper')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_broker",
                schema: "trading",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_mode",
                schema: "trading",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_broker",
                schema: "trading",
                table: "orders",
                sql: "broker IN ('mock', 'etoro')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_mode",
                schema: "trading",
                table: "orders",
                sql: "mode IN ('simulated', 'etoro_demo')");
        }
    }
}
