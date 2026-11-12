using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Ai.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "analysis_jobs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "1d"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_jobs", x => x.id);
                    table.CheckConstraint("ck_analysis_jobs_kind", "kind IN ('stock_analysis','signal_explanation','news_summary')");
                    table.CheckConstraint("ck_analysis_jobs_status", "status IN ('pending','processing','completed','failed','cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "ai_requests",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_requests", x => x.id);
                    table.CheckConstraint("ck_ai_requests_outcome", "outcome IN ('ok','timeout','http_error','parse_error','schema_error','cancelled')");
                    table.ForeignKey(
                        name: "fk_ai_requests_analysis_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "ai",
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "analysis_results",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_digest = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prompt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    output_schema_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    raw_output = table.Column<string>(type: "text", nullable: false),
                    parsed = table.Column<string>(type: "jsonb", nullable: true),
                    validation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    validation_issues = table.Column<string>(type: "jsonb", nullable: false),
                    sentiment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    data_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    analysis_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level_validation = table.Column<string>(type: "jsonb", nullable: true),
                    reward_risk = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    stale = table.Column<bool>(type: "boolean", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_results", x => x.id);
                    table.CheckConstraint("ck_analysis_results_confidence", "confidence IS NULL OR confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_analysis_results_sentiment", "sentiment IS NULL OR sentiment IN ('bullish','bearish','neutral','mixed')");
                    table.CheckConstraint("ck_analysis_results_validation_status", "validation_status IN ('valid','valid_with_rejections','invalid')");
                    table.ForeignKey(
                        name: "fk_analysis_results_analysis_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "ai",
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_requests_job_id",
                schema: "ai",
                table: "ai_requests",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_requests_started_at",
                schema: "ai",
                table: "ai_requests",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_claim",
                schema: "ai",
                table: "analysis_jobs",
                columns: new[] { "status", "run_after" },
                filter: "status IN ('pending','processing')");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_user_id_created_at",
                schema: "ai",
                table: "analysis_jobs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_user_id_idempotency_key",
                schema: "ai",
                table: "analysis_jobs",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_analysis_jobs_one_active",
                schema: "ai",
                table: "analysis_jobs",
                columns: new[] { "user_id", "instrument_id", "kind" },
                unique: true,
                filter: "status IN ('pending','processing')");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_results_job_id",
                schema: "ai",
                table: "analysis_results",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analysis_results_user_id_instrument_id_analysis_at",
                schema: "ai",
                table: "analysis_results",
                columns: new[] { "user_id", "instrument_id", "analysis_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_requests",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "analysis_results",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "analysis_jobs",
                schema: "ai");
        }
    }
}
