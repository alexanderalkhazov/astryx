using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlpacaBrokerage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolios_mode",
                schema: "portfolio",
                table: "portfolios");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolios_mode",
                schema: "portfolio",
                table: "portfolios",
                sql: "mode IN ('simulated', 'alpaca_paper')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolios_mode",
                schema: "portfolio",
                table: "portfolios");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolios_mode",
                schema: "portfolio",
                table: "portfolios",
                sql: "mode IN ('simulated', 'etoro_demo')");
        }
    }
}
