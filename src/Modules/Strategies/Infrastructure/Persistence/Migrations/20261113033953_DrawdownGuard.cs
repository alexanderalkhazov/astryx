using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Strategies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DrawdownGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guard_announced_status",
                schema: "strategy",
                table: "strategies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "guard_as_of",
                schema: "strategy",
                table: "strategies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "guard_drawdown",
                schema: "strategy",
                table: "strategies",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "guard_pause_pct",
                schema: "strategy",
                table: "strategies",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: -0.0600m);

            migrationBuilder.AddColumn<int>(
                name: "guard_window_days",
                schema: "strategy",
                table: "strategies",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "live_points",
                schema: "strategy",
                columns: table => new
                {
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    d = table.Column<DateOnly>(type: "date", nullable: false),
                    equity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_points", x => new { x.strategy_id, x.d });
                    table.CheckConstraint("ck_live_points_equity", "equity > 0");
                    table.ForeignKey(
                        name: "fk_live_points_strategies_strategy_id",
                        column: x => x.strategy_id,
                        principalSchema: "strategy",
                        principalTable: "strategies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The illustrative live paper track (T-097); the guard job derives status and announcements from it.
            foreach (var statement in Astryx.Modules.Strategies.Infrastructure.Seed.CatalogSeed.LivePointStatements())
            {
                migrationBuilder.Sql(statement);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_points",
                schema: "strategy");

            migrationBuilder.DropColumn(
                name: "guard_announced_status",
                schema: "strategy",
                table: "strategies");

            migrationBuilder.DropColumn(
                name: "guard_as_of",
                schema: "strategy",
                table: "strategies");

            migrationBuilder.DropColumn(
                name: "guard_drawdown",
                schema: "strategy",
                table: "strategies");

            migrationBuilder.DropColumn(
                name: "guard_pause_pct",
                schema: "strategy",
                table: "strategies");

            migrationBuilder.DropColumn(
                name: "guard_window_days",
                schema: "strategy",
                table: "strategies");
        }
    }
}
