using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PushDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_devices",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    push_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    platform = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_devices", x => x.id);
                    table.CheckConstraint("ck_push_devices_platform", "platform IN ('ios', 'android')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_push_devices_push_token",
                schema: "notify",
                table: "push_devices",
                column: "push_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_devices_user_id_device_id",
                schema: "notify",
                table: "push_devices",
                columns: new[] { "user_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_devices",
                schema: "notify");
        }
    }
}
