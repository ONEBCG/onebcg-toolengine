using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolFullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ApprovalToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OtpHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Risk = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailedOtpAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AcknowledgementJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SerializedResult = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    DenialReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingApprovals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScenarioExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ScenarioVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SuspendedAtStepId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PendingApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailedAtStepId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StepContextJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    InputJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    InitiatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolInvocationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvocationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CallerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GovernanceMetadataJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolInvocationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolInvocationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ToolFullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ToolVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InvokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    TokensUsed = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CallerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GovernanceMetadataJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolInvocationRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_RetryCount",
                table: "OutboxMessages",
                column: "RetryCount");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_SentAt",
                table: "OutboxMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ApprovalToken",
                table: "PendingApprovals",
                column: "ApprovalToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ExpiresAt",
                table: "PendingApprovals",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_IdempotencyKey",
                table: "PendingApprovals",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Status",
                table: "PendingApprovals",
                column: "Status");

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

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationEvents_InvocationRecordId",
                table: "ToolInvocationEvents",
                column: "InvocationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationEvents_OccurredAt",
                table: "ToolInvocationEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationRecords_RetainUntil",
                table: "ToolInvocationRecords",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationRecords_ToolFullName_InvokedAt",
                table: "ToolInvocationRecords",
                columns: new[] { "ToolFullName", "InvokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PendingApprovals");

            migrationBuilder.DropTable(
                name: "ScenarioExecutions");

            migrationBuilder.DropTable(
                name: "ToolInvocationEvents");

            migrationBuilder.DropTable(
                name: "ToolInvocationRecords");
        }
    }
}
