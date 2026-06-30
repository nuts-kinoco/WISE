using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderDiagnosticsAndProcessingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Works",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProviderDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    Strategy = table.Column<string>(type: "TEXT", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalResponseTimeMs = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureReason = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDiagnostics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDiagnostics_ProviderId",
                table: "ProviderDiagnostics",
                column: "ProviderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderDiagnostics");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Works");
        }
    }
}
