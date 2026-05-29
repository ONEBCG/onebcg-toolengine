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
                name: "KycScreeningRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    QueryRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    MatchResult = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MatchScore = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    MatchedEntity = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ListMatched = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ScreenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OfficerDecision = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OfficerUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycScreeningRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayeeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SwiftBic = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    RoutingCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OnboardedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    KycRefreshedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayeeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EnteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExitedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentInstructions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayerJurisdiction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    PayerEntityId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayeeRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    VerifiedPayeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ServiceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PpmId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InitiatorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentStage = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    WhtRate = table.Column<decimal>(type: "decimal(7,4)", nullable: true),
                    WhtAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NetPayableAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    WhtConfidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    WhtJustification = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ApplicableTreaty = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ServiceClassification = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    KycResult = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    KycMatchScore = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    KycScreeningRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PendingApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovalTier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BankTransactionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SubmittedToBankAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    BlockReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInstructions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolFullName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ApprovalToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OtpHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FailedOtpAttempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AcknowledgementJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    SerializedResult = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    DenialReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingApprovals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpmContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PpmId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PayerEntityId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermittedServiceTypes = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ApprovedCurrencies = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MaxSingleTransaction = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AggregateCapAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CumulativePaid = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContractDocumentPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ContractVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpmContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolInvocationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvocationRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CallerType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GovernanceMetadataJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolInvocationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolInvocationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ToolFullName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ToolVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InvokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    TokensUsed = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CallerType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GovernanceMetadataJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolInvocationRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhtRateEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayerCountry = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    PayeeCountry = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    ServiceCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TreatyArticle = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StandardRatePct = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    ReducedTreatyRatePct = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    ConditionsForReduced = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    TreatyExists = table.Column<bool>(type: "INTEGER", nullable: false),
                    RuleVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhtRateEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KycScreeningRecords_MatchResult",
                table: "KycScreeningRecords",
                column: "MatchResult");

            migrationBuilder.CreateIndex(
                name: "IX_KycScreeningRecords_PaymentId",
                table: "KycScreeningRecords",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KycScreeningRecords_ScreenedAt",
                table: "KycScreeningRecords",
                column: "ScreenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_RetryCount",
                table: "OutboxMessages",
                column: "RetryCount");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_SentAt",
                table: "OutboxMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_PayeeRecords_Jurisdiction_Status",
                table: "PayeeRecords",
                columns: new[] { "Jurisdiction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayeeRecords_LegalName",
                table: "PayeeRecords",
                column: "LegalName");

            migrationBuilder.CreateIndex(
                name: "IX_PayeeRecords_TaxIdentifier",
                table: "PayeeRecords",
                column: "TaxIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuditLogs_PaymentId",
                table: "PaymentAuditLogs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuditLogs_PaymentId_Stage",
                table: "PaymentAuditLogs",
                columns: new[] { "PaymentId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstructions_InitiatedAt",
                table: "PaymentInstructions",
                column: "InitiatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstructions_RetainUntil",
                table: "PaymentInstructions",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstructions_VerifiedPayeeId",
                table: "PaymentInstructions",
                column: "VerifiedPayeeId");

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
                name: "IX_PpmContracts_IsActive_EffectiveTo",
                table: "PpmContracts",
                columns: new[] { "IsActive", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_PpmContracts_PayeeId",
                table: "PpmContracts",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PpmContracts_PpmId_PayeeId",
                table: "PpmContracts",
                columns: new[] { "PpmId", "PayeeId" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_WhtRateEntries_PayerCountry_PayeeCountry_ServiceCategory",
                table: "WhtRateEntries",
                columns: new[] { "PayerCountry", "PayeeCountry", "ServiceCategory" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KycScreeningRecords");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PayeeRecords");

            migrationBuilder.DropTable(
                name: "PaymentAuditLogs");

            migrationBuilder.DropTable(
                name: "PaymentInstructions");

            migrationBuilder.DropTable(
                name: "PendingApprovals");

            migrationBuilder.DropTable(
                name: "PpmContracts");

            migrationBuilder.DropTable(
                name: "ToolInvocationEvents");

            migrationBuilder.DropTable(
                name: "ToolInvocationRecords");

            migrationBuilder.DropTable(
                name: "WhtRateEntries");
        }
    }
}
