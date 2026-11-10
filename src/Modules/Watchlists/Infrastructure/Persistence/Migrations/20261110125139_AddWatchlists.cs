using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Watchlists.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "watchlist");

            migrationBuilder.CreateTable(
                name: "watchlists",
                schema: "watchlist",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlists", x => x.id);
                    table.CheckConstraint("ck_watchlists_name_length", "char_length(name) BETWEEN 1 AND 40");
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlists_user_id_name",
                schema: "watchlist",
                table: "watchlists",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_watchlists_one_default",
                schema: "watchlist",
                table: "watchlists",
                column: "user_id",
                unique: true,
                filter: "is_default");

            // Cross-schema FK to identity.users (owned by the Identity module, migrated first): account erasure cascades.
            migrationBuilder.Sql("""
                ALTER TABLE watchlist.watchlists ADD CONSTRAINT fk_watchlists_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watchlists",
                schema: "watchlist");
        }
    }
}
