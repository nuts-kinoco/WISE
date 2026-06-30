using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeAndWorkFavoriteRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Favorite",
                table: "Works",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Works",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetType",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Favorite",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "Assets");
        }
    }
}
