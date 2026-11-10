using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_queue",
                schema: "infra",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dedupe_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_queue", x => x.id);
                    table.CheckConstraint("ck_job_queue_attempts", "attempts >= 0 AND max_attempts >= 1 AND timeout_seconds >= 1");
                    table.CheckConstraint("ck_job_queue_status", "status IN ('pending', 'running', 'succeeded', 'dead')");
                });

            migrationBuilder.CreateTable(
                name: "recurring_jobs",
                schema: "infra",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_jobs", x => x.name);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_queue_due",
                schema: "infra",
                table: "job_queue",
                column: "run_after",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_job_queue_leases",
                schema: "infra",
                table: "job_queue",
                column: "locked_until",
                filter: "status = 'running'");

            migrationBuilder.CreateIndex(
                name: "ux_job_queue_active_dedupe",
                schema: "infra",
                table: "job_queue",
                column: "dedupe_key",
                unique: true,
                filter: "status IN ('pending', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_queue",
                schema: "infra");

            migrationBuilder.DropTable(
                name: "recurring_jobs",
                schema: "infra");
        }
    }
}
