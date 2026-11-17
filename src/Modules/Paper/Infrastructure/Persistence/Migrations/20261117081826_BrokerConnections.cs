using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Paper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrokerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broker_connections",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    environment = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    account_ref = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    key_suffix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    credentials_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    credentials_key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_connections", x => x.id);
                    table.CheckConstraint("ck_broker_connections_broker", "broker IN ('alpaca')");
                    table.CheckConstraint("ck_broker_connections_environment", "environment IN ('paper')");
                    table.CheckConstraint("ck_broker_connections_status", "status IN ('connected', 'degraded', 'revoked')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_broker_connections_one_active",
                schema: "trading",
                table: "broker_connections",
                columns: new[] { "user_id", "broker", "environment" },
                unique: true,
                filter: "status <> 'revoked'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_connections",
                schema: "trading");
        }
    }
}
