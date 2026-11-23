using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Strategies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrategyTrackRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "track_record",
                schema: "strategy",
                table: "strategies",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "track_record",
                schema: "strategy",
                table: "strategies");
        }
    }
}
