using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobProgressAndResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProcessedCount",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultPayload",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCount",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedCount",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ResultPayload",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "TotalCount",
                table: "Jobs");
        }
    }
}
