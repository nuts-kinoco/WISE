using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint14_MediaTypeRoleStorageFormatReadingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaType",
                table: "Works",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StorageFormat",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CoverCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    CachedPath = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoverCaches_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisplayProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverOrientation = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultSort = table.Column<string>(type: "TEXT", nullable: false),
                    IsUserCustomized = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisplayProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PositionSeconds = table.Column<float>(type: "REAL", nullable: true),
                    PositionPercent = table.Column<float>(type: "REAL", nullable: true),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingHistories_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisplayProfileFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisplayProfileFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisplayProfileFields_DisplayProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "DisplayProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoverCaches_WorkId",
                table: "CoverCaches",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_CoverCaches_WorkId_ProviderName",
                table: "CoverCaches",
                columns: new[] { "WorkId", "ProviderName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisplayProfileFields_ProfileId_DisplayOrder",
                table: "DisplayProfileFields",
                columns: new[] { "ProfileId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DisplayProfileFields_ProfileId_FieldName",
                table: "DisplayProfileFields",
                columns: new[] { "ProfileId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisplayProfiles_MediaType",
                table: "DisplayProfiles",
                column: "MediaType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingHistories_DeviceId_LastReadAt",
                table: "ReadingHistories",
                columns: new[] { "DeviceId", "LastReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingHistories_WorkId",
                table: "ReadingHistories",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingHistories_WorkId_DeviceId",
                table: "ReadingHistories",
                columns: new[] { "WorkId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoverCaches");

            migrationBuilder.DropTable(
                name: "DisplayProfileFields");

            migrationBuilder.DropTable(
                name: "ReadingHistories");

            migrationBuilder.DropTable(
                name: "DisplayProfiles");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "Works");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "StorageFormat",
                table: "Assets");
        }
    }
}
