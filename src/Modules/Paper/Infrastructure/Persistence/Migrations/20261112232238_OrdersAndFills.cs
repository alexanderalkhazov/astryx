using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Paper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersAndFills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    broker = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    broker_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    order_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    time_in_force = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stop_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    limit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    oco_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    filled_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    avg_fill_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    reject_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    filled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reserved_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_broker", "broker IN ('mock', 'etoro')");
                    table.CheckConstraint("ck_orders_entry_assessed", "purpose <> 'entry' OR (proposal_id IS NOT NULL AND assessment_id IS NOT NULL)");
                    table.CheckConstraint("ck_orders_mode", "mode IN ('simulated', 'etoro_demo')");
                    table.CheckConstraint("ck_orders_purpose", "purpose IN ('entry', 'exit_stop', 'exit_target', 'exit_manual', 'exit_reset')");
                    table.CheckConstraint("ck_orders_quantity", "quantity > 0");
                    table.CheckConstraint("ck_orders_side", "side IN ('buy', 'sell')");
                    table.CheckConstraint("ck_orders_status", "status IN ('accepted', 'submitting', 'submitted', 'working', 'filled', 'cancelled', 'expired', 'rejected')");
                    table.CheckConstraint("ck_orders_tif", "time_in_force IN ('day', 'gtc')");
                    table.CheckConstraint("ck_orders_type", "order_type IN ('market', 'limit', 'buy_stop', 'sell_stop')");
                });

            migrationBuilder.CreateTable(
                name: "fills",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reference_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    slippage_per_share = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    quote_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    filled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    simulation_model = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fills", x => x.id);
                    table.CheckConstraint("ck_fills_slippage", "slippage_per_share >= 0");
                    table.ForeignKey(
                        name: "fk_fills_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "trading",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fills_order_id_sequence",
                schema: "trading",
                table: "fills",
                columns: new[] { "order_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fills_portfolio_id_filled_at",
                schema: "trading",
                table: "fills",
                columns: new[] { "portfolio_id", "filled_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_broker_broker_order_id",
                schema: "trading",
                table: "orders",
                columns: new[] { "broker", "broker_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_portfolio_id_submitted_at",
                schema: "trading",
                table: "orders",
                columns: new[] { "portfolio_id", "submitted_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_client_order_id",
                schema: "trading",
                table: "orders",
                columns: new[] { "user_id", "client_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_working",
                schema: "trading",
                table: "orders",
                column: "status",
                filter: "status = 'working'");

            migrationBuilder.CreateIndex(
                name: "ux_orders_one_entry_per_proposal",
                schema: "trading",
                table: "orders",
                column: "proposal_id",
                unique: true,
                filter: "purpose = 'entry'");

            migrationBuilder.CreateIndex(
                name: "ux_orders_one_live_exit",
                schema: "trading",
                table: "orders",
                columns: new[] { "position_id", "purpose" },
                unique: true,
                filter: "status IN ('accepted', 'working') AND purpose LIKE 'exit_%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fills",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "trading");
        }
    }
}
