using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siniestros360.AdjustersService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClaimRecords : Migration
    {
        /// <inheritdoc />
        // Renombre en vez de DropTable/CreateTable: conserva los siniestros ya terminados.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_FinishedClaims", schema: "adjusters", table: "FinishedClaims");
            migrationBuilder.RenameTable(name: "FinishedClaims", schema: "adjusters", newName: "ClaimRecords", newSchema: "adjusters");
            migrationBuilder.AlterColumn<DateTimeOffset>(name: "FinishedAt", schema: "adjusters", table: "ClaimRecords", type: "timestamp with time zone", nullable: true, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone");
            migrationBuilder.AddColumn<Guid>(name: "AdjusterId", schema: "adjusters", table: "ClaimRecords", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "UpdatedAt", schema: "adjusters", table: "ClaimRecords", type: "timestamp with time zone", nullable: false, defaultValueSql: "now()");
            migrationBuilder.Sql("UPDATE adjusters.\"ClaimRecords\" SET \"UpdatedAt\" = \"FinishedAt\" WHERE \"FinishedAt\" IS NOT NULL;");
            migrationBuilder.AlterColumn<DateTimeOffset>(name: "UpdatedAt", schema: "adjusters", table: "ClaimRecords", type: "timestamp with time zone", nullable: false, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldDefaultValueSql: "now()");
            // xmin es columna de sistema de PostgreSQL: Npgsql no genera SQL para ella.
            migrationBuilder.AddColumn<uint>(name: "xmin", schema: "adjusters", table: "ClaimRecords", type: "xid", rowVersion: true, nullable: false, defaultValue: 0u);
            migrationBuilder.AddPrimaryKey(name: "PK_ClaimRecords", schema: "adjusters", table: "ClaimRecords", column: "ClaimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ClaimRecords", schema: "adjusters", table: "ClaimRecords");
            migrationBuilder.Sql("DELETE FROM adjusters.\"ClaimRecords\" WHERE \"FinishedAt\" IS NULL;");
            migrationBuilder.DropColumn(name: "xmin", schema: "adjusters", table: "ClaimRecords");
            migrationBuilder.DropColumn(name: "UpdatedAt", schema: "adjusters", table: "ClaimRecords");
            migrationBuilder.DropColumn(name: "AdjusterId", schema: "adjusters", table: "ClaimRecords");
            migrationBuilder.AlterColumn<DateTimeOffset>(name: "FinishedAt", schema: "adjusters", table: "ClaimRecords", type: "timestamp with time zone", nullable: false, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldNullable: true);
            migrationBuilder.RenameTable(name: "ClaimRecords", schema: "adjusters", newName: "FinishedClaims", newSchema: "adjusters");
            migrationBuilder.AddPrimaryKey(name: "PK_FinishedClaims", schema: "adjusters", table: "FinishedClaims", column: "ClaimId");
        }
    }
}
