using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siniestros360.DispatchService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrchestratePolicyValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverageReason",
                schema: "dispatch",
                table: "AssignmentSagas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverageStatus",
                schema: "dispatch",
                table: "AssignmentSagas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoverageUpdatedAt",
                schema: "dispatch",
                table: "AssignmentSagas",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverageReason",
                schema: "dispatch",
                table: "AssignmentSagas");

            migrationBuilder.DropColumn(
                name: "CoverageStatus",
                schema: "dispatch",
                table: "AssignmentSagas");

            migrationBuilder.DropColumn(
                name: "CoverageUpdatedAt",
                schema: "dispatch",
                table: "AssignmentSagas");
        }
    }
}
