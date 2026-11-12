using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Discovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecentSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "discovery");

            migrationBuilder.CreateTable(
                name: "recent_searches",
                schema: "discovery",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    searched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recent_searches", x => new { x.user_id, x.instrument_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_recent_searches_user_id_searched_at",
                schema: "discovery",
                table: "recent_searches",
                columns: new[] { "user_id", "searched_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recent_searches",
                schema: "discovery");
        }
    }
}
