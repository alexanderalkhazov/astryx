using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Discovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignalStrategyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "strategy_version",
                schema: "discovery",
                table: "signals",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "strategy_version",
                schema: "discovery",
                table: "signals");
        }
    }
}
