using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WISE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HISTORY_RECORD",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    correlation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    work_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HISTORY_RECORD", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "JOB_DEFINITION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobType = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration = table.Column<string>(type: "TEXT", nullable: false),
                    TargetWorkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_DEFINITION", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JOB_EXECUTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_EXECUTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JOB_EXECUTION_JOB_DEFINITION_JobDefinitionId",
                        column: x => x.JobDefinitionId,
                        principalTable: "JOB_DEFINITION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JOB_LOG",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogLevel = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_LOG", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JOB_LOG_JOB_EXECUTION_JobExecutionId",
                        column: x => x.JobExecutionId,
                        principalTable: "JOB_EXECUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HISTORY_RECORD_asset_id",
                table: "HISTORY_RECORD",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_HISTORY_RECORD_occurred_at",
                table: "HISTORY_RECORD",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_HISTORY_RECORD_work_id",
                table: "HISTORY_RECORD",
                column: "work_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_DEFINITION_JobType",
                table: "JOB_DEFINITION",
                column: "JobType");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_EXECUTION_CorrelationId",
                table: "JOB_EXECUTION",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_EXECUTION_JobDefinitionId",
                table: "JOB_EXECUTION",
                column: "JobDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_EXECUTION_Status",
                table: "JOB_EXECUTION",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_LOG_JobExecutionId",
                table: "JOB_LOG",
                column: "JobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchFolders_Path",
                table: "WatchFolders",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HISTORY_RECORD");

            migrationBuilder.DropTable(
                name: "JOB_LOG");

            migrationBuilder.DropTable(
                name: "WatchFolders");

            migrationBuilder.DropTable(
                name: "JOB_EXECUTION");

            migrationBuilder.DropTable(
                name: "JOB_DEFINITION");
        }
    }
}
