using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siniestros360.OperationsService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdjusterRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "operations",
                table: "Adjusters",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "operations",
                table: "Adjusters");
        }
    }
}
