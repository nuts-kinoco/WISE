using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchFolderProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdentifierProfileId",
                table: "WatchFolders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MetadataPipelineProfileId",
                table: "WatchFolders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleProfileId",
                table: "WatchFolders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentifierProfileId",
                table: "WatchFolders");

            migrationBuilder.DropColumn(
                name: "MetadataPipelineProfileId",
                table: "WatchFolders");

            migrationBuilder.DropColumn(
                name: "RuleProfileId",
                table: "WatchFolders");
        }
    }
}
