using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint24_PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Works_Favorite",
                table: "Works",
                column: "Favorite");

            migrationBuilder.CreateIndex(
                name: "IX_Works_MediaType",
                table: "Works",
                column: "MediaType");

            migrationBuilder.CreateIndex(
                name: "IX_Works_Status",
                table: "Works",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataFields_FieldName_Value",
                table: "MetadataFields",
                columns: new[] { "FieldName", "Value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Works_Favorite",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_Works_MediaType",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_Works_Status",
                table: "Works");

            migrationBuilder.DropIndex(
                name: "IX_MetadataFields_FieldName_Value",
                table: "MetadataFields");
        }
    }
}
