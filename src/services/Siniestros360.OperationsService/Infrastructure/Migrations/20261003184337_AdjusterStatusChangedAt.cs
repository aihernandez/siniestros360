using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siniestros360.OperationsService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdjusterStatusChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAt",
                schema: "operations",
                table: "Adjusters",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                schema: "operations",
                table: "Adjusters");
        }
    }
}
