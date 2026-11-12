using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Trading.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Proposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "proposals",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    order_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    time_in_force = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stop_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    copy_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    invalidation = table.Column<string>(type: "jsonb", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    reject_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                    table.CheckConstraint("ck_proposals_copy_source", "copy_source IN ('template', 'ai')");
                    table.CheckConstraint("ck_proposals_levels", "stop_price < entry_price AND entry_price < target_price");
                    table.CheckConstraint("ck_proposals_order_type", "order_type IN ('buy_stop', 'limit', 'market')");
                    table.CheckConstraint("ck_proposals_reject_reason", "reject_reason IS NULL OR reject_reason IN ('user_rejected', 'expired', 'superseded')");
                    table.CheckConstraint("ck_proposals_side", "side = 'buy'");
                    table.CheckConstraint("ck_proposals_source", "(source = 'signal' AND signal_id IS NOT NULL) OR (source = 'analysis' AND analysis_result_id IS NOT NULL)");
                    table.CheckConstraint("ck_proposals_status", "status IN ('pending', 'approved', 'rejected', 'expired')");
                    table.CheckConstraint("ck_proposals_tif", "time_in_force = 'day'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_expiry",
                schema: "trading",
                table: "proposals",
                column: "expires_at",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_user_id_status_created_at",
                schema: "trading",
                table: "proposals",
                columns: new[] { "user_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_proposals_one_pending",
                schema: "trading",
                table: "proposals",
                columns: new[] { "user_id", "instrument_id" },
                unique: true,
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposals",
                schema: "trading");
        }
    }
}
