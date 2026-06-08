using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PayeeRecords ──────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PayeeRecords",
                columns: table => new
                {
                    Id                 = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName          = table.Column<string>(type: "text", maxLength: 512, nullable: false),
                    Jurisdiction       = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                    EntityType         = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                    Status             = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                    TaxIdentifier      = table.Column<string>(type: "text", maxLength: 128, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "text", maxLength: 128, nullable: true),
                    BankAccountNumber  = table.Column<string>(type: "text", maxLength: 64, nullable: true),
                    Iban               = table.Column<string>(type: "text", maxLength: 64, nullable: true),
                    SwiftBic           = table.Column<string>(type: "text", maxLength: 16, nullable: true),
                    RoutingCode        = table.Column<string>(type: "text", maxLength: 64, nullable: true),
                    OnboardedAt        = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KycRefreshedAt     = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt          = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt          = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PayeeRecords", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_PayeeRecords_LegalName",
                table: "PayeeRecords", column: "LegalName");
            migrationBuilder.CreateIndex(name: "IX_PayeeRecords_TaxIdentifier",
                table: "PayeeRecords", column: "TaxIdentifier");
            migrationBuilder.CreateIndex(name: "IX_PayeeRecords_Jurisdiction_Status",
                table: "PayeeRecords", columns: ["Jurisdiction", "Status"]);

            // ── PpmContracts ──────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PpmContracts",
                columns: table => new
                {
                    Id                    = table.Column<Guid>(type: "uuid", nullable: false),
                    PpmId                 = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    PayerEntityId         = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    PayeeId               = table.Column<Guid>(type: "uuid", nullable: false),
                    PermittedServiceTypes = table.Column<string>(type: "text", maxLength: 1024, nullable: false),
                    ApprovedCurrencies    = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    MaxSingleTransaction  = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AggregateCapAmount    = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CumulativePaid        = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 0m),
                    EffectiveFrom         = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo           = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive              = table.Column<bool>(type: "boolean", nullable: false),
                    ContractDocumentPath  = table.Column<string>(type: "text", maxLength: 2048, nullable: true),
                    ContractVersion       = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    CreatedAt             = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt             = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PpmContracts", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_PpmContracts_PayeeId",
                table: "PpmContracts", column: "PayeeId");
            migrationBuilder.CreateIndex(name: "IX_PpmContracts_PpmId_PayeeId",
                table: "PpmContracts", columns: ["PpmId", "PayeeId"], unique: true);
            migrationBuilder.CreateIndex(name: "IX_PpmContracts_IsActive_EffectiveTo",
                table: "PpmContracts", columns: ["IsActive", "EffectiveTo"]);

            // ── WhtRateEntries ────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "WhtRateEntries",
                columns: table => new
                {
                    Id                   = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerCountry         = table.Column<string>(type: "text", maxLength: 4, nullable: false),
                    PayeeCountry         = table.Column<string>(type: "text", maxLength: 4, nullable: false),
                    ServiceCategory      = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    TreatyArticle        = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    StandardRatePct      = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    ReducedTreatyRatePct = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    ConditionsForReduced = table.Column<string>(type: "text", maxLength: 1024, nullable: true),
                    TreatyExists         = table.Column<bool>(type: "boolean", nullable: false),
                    RuleVersion          = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                    CreatedAt            = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt            = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_WhtRateEntries", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_WhtRateEntries_PayerCountry_PayeeCountry_ServiceCategory",
                table: "WhtRateEntries", columns: ["PayerCountry", "PayeeCountry", "ServiceCategory"], unique: true);

            // ── PaymentInstructions ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PaymentInstructions",
                columns: table => new
                {
                    Id                    = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerName             = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    PayerJurisdiction     = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                    PayerEntityId         = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    PayeeRef              = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    VerifiedPayeeId       = table.Column<Guid>(type: "uuid", nullable: true),
                    GrossAmount           = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Currency              = table.Column<string>(type: "text", maxLength: 3, nullable: false),
                    ServiceType           = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    PpmId                 = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    InitiatorId           = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    InitiatedAt           = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status                = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    CurrentStage          = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    // Stage 3 — WHT
                    WhtRate               = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    WhtAmount             = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    NetPayableAmount      = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    WhtConfidence         = table.Column<string>(type: "text", maxLength: 32, nullable: true),
                    WhtJustification      = table.Column<string>(type: "text", maxLength: 2048, nullable: true),
                    ApplicableTreaty      = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    ServiceClassification = table.Column<string>(type: "text", maxLength: 128, nullable: true),
                    // Stage 4 — KYC
                    KycResult             = table.Column<string>(type: "text", maxLength: 32, nullable: true),
                    KycMatchScore         = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    KycScreeningRef       = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    // Stage 5 — Approval
                    PendingApprovalId     = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalTier          = table.Column<string>(type: "text", maxLength: 64, nullable: true),
                    // Stage 6 — Execution
                    BankTransactionId     = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    SubmittedToBankAt     = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SettledAt             = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    // Exception / GDPR
                    BlockReason           = table.Column<string>(type: "text", maxLength: 1024, nullable: true),
                    RetainUntil           = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt             = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt             = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PaymentInstructions", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_PaymentInstructions_InitiatedAt",
                table: "PaymentInstructions", column: "InitiatedAt");
            migrationBuilder.CreateIndex(name: "IX_PaymentInstructions_RetainUntil",
                table: "PaymentInstructions", column: "RetainUntil");
            migrationBuilder.CreateIndex(name: "IX_PaymentInstructions_VerifiedPayeeId",
                table: "PaymentInstructions", column: "VerifiedPayeeId");

            // ── KycScreeningRecords ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "KycScreeningRecords",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId       = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName    = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    QueryRef        = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    MatchResult     = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                    MatchScore      = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    MatchedEntity   = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    ListMatched     = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    ScreenedAt      = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OfficerDecision = table.Column<string>(type: "text", maxLength: 1024, nullable: true),
                    OfficerUserId   = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    CreatedAt       = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt       = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_KycScreeningRecords", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_KycScreeningRecords_PaymentId",
                table: "KycScreeningRecords", column: "PaymentId");
            migrationBuilder.CreateIndex(name: "IX_KycScreeningRecords_MatchResult",
                table: "KycScreeningRecords", column: "MatchResult");
            migrationBuilder.CreateIndex(name: "IX_KycScreeningRecords_ScreenedAt",
                table: "KycScreeningRecords", column: "ScreenedAt");

            // ── PaymentAuditLogs ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PaymentAuditLogs",
                columns: table => new
                {
                    Id        = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage     = table.Column<int>(type: "integer", nullable: false),
                    StageName = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    Outcome   = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                    Details   = table.Column<string>(type: "text", maxLength: 4096, nullable: true),
                    ActorId   = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExitedAt  = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PaymentAuditLogs", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_PaymentAuditLogs_PaymentId",
                table: "PaymentAuditLogs", column: "PaymentId");
            migrationBuilder.CreateIndex(name: "IX_PaymentAuditLogs_PaymentId_Stage",
                table: "PaymentAuditLogs", columns: ["PaymentId", "Stage"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PaymentAuditLogs");
            migrationBuilder.DropTable(name: "KycScreeningRecords");
            migrationBuilder.DropTable(name: "PaymentInstructions");
            migrationBuilder.DropTable(name: "WhtRateEntries");
            migrationBuilder.DropTable(name: "PpmContracts");
            migrationBuilder.DropTable(name: "PayeeRecords");
        }
    }
}
