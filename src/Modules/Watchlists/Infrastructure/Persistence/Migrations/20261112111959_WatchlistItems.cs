using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Watchlists.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WatchlistItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "watchlist",
                table: "watchlists",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "watchlist_items",
                schema: "watchlist",
                columns: table => new
                {
                    watchlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_items", x => new { x.watchlist_id, x.instrument_id });
                    table.CheckConstraint("ck_watchlist_items_note_length", "note IS NULL OR char_length(note) <= 280");
                    table.ForeignKey(
                        name: "fk_watchlist_items_watchlists_watchlist_id",
                        column: x => x.watchlist_id,
                        principalSchema: "watchlist",
                        principalTable: "watchlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_items_instrument_id",
                schema: "watchlist",
                table: "watchlist_items",
                column: "instrument_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watchlist_items",
                schema: "watchlist");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "watchlist",
                table: "watchlists");
        }
    }
}
