using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Quant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicatorSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "market");

            migrationBuilder.CreateTable(
                name: "indicator_snapshots",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    as_of_bar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    params_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    data_quality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_snapshots", x => x.id);
                    table.CheckConstraint("ck_indicator_snapshots_data_quality", "data_quality IN ('ok', 'gaps', 'stale', 'insufficient')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_indicator_snapshots_key",
                schema: "market",
                table: "indicator_snapshots",
                columns: new[] { "instrument_id", "timeframe", "params_hash", "as_of_bar" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indicator_snapshots",
                schema: "market");
        }
    }
}
