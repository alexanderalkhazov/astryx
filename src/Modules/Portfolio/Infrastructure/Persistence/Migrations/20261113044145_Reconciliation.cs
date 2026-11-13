using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Reconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconciled_at",
                schema: "portfolio",
                table: "portfolios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconciliation_halted_at",
                schema: "portfolio",
                table: "portfolios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reconciliation_issues",
                schema: "portfolio",
                table: "portfolios",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reconciled_at",
                schema: "portfolio",
                table: "portfolios");

            migrationBuilder.DropColumn(
                name: "reconciliation_halted_at",
                schema: "portfolio",
                table: "portfolios");

            migrationBuilder.DropColumn(
                name: "reconciliation_issues",
                schema: "portfolio",
                table: "portfolios");
        }
    }
}
