using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScenarioExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ScenarioVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SuspendedAtStepId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PendingApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FailedAtStepId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StepContextJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    InputJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    InitiatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioExecutions_CreatedAt",
                table: "ScenarioExecutions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioExecutions_PendingApprovalId",
                table: "ScenarioExecutions",
                column: "PendingApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioExecutions_ScenarioName_Status",
                table: "ScenarioExecutions",
                columns: new[] { "ScenarioName", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScenarioExecutions");
        }
    }
}
