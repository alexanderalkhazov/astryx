using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PositionsStrategyExit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_exit_reason",
                schema: "portfolio",
                table: "positions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_exit_reason",
                schema: "portfolio",
                table: "positions",
                sql: "exit_reason IS NULL OR exit_reason IN ('stop', 'target', 'manual', 'reset', 'strategy')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_exit_reason",
                schema: "portfolio",
                table: "positions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_exit_reason",
                schema: "portfolio",
                table: "positions",
                sql: "exit_reason IS NULL OR exit_reason IN ('stop', 'target', 'manual', 'reset')");
        }
    }
}
