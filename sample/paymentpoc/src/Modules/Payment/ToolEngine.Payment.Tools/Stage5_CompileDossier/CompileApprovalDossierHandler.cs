using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Attributes;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage5_CompileDossier;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record CompileApprovalDossierInput(Guid PaymentId);

public sealed record CompileApprovalDossierOutput(
    Guid   PaymentId,
    string ApprovalTier,
    string DossierSummary,
    bool   KycClear,
    bool   WhtFlagged,
    string Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 5 — Compile Payment Dossier and route to Approval Gate (Composite).
///
/// [RequiresApproval(High)] triggers ToolEngine's ApprovalBehavior in the MediatR pipeline.
/// The behavior creates a PendingApproval record and returns ToolResponse.Suspended —
/// this causes the API to return HTTP 202 with a Location header and Retry-After.
///
/// Approval tier routing (spec §4 Stage 5):
///   Net payable < $10,000  → auto-approved if all stages GREEN
///   $10,000 – $100,000     → Single Finance Approver
///   $100,000 – $500,000    → Finance Approver + CFO
///   > $500,000             → Finance Approver + CFO + Board Designee
///
/// Barrier: Confirmed KYC block in dossier → approval UI will not allow approval.
/// The dossier is compiled here; the actual approval is handled by the standard
/// ToolEngine approval endpoint (POST /approvals/{id}/approve).
/// </summary>
[RequiresApproval(
    Risk:    Core.Domain.Enums.ApprovalRisk.High,
    Channel: Core.Domain.Enums.ApprovalChannel.Dashboard,
    Reason:  "Payment release requires human sign-off. Dossier compiled from all pipeline stages.")]
public sealed class CompileApprovalDossierHandler
    : CompositeToolBase<CompileApprovalDossierInput, CompileApprovalDossierOutput>
{
    private readonly Infrastructure.Persistence.AppDbContext _db;

    public CompileApprovalDossierHandler(
        IToolExecutor toolExecutor,
        Infrastructure.Persistence.AppDbContext db)
        : base(toolExecutor) => _db = db;

    public override string    Namespace => "payment";
    public override string    Name      => "compile-dossier";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Compiles a complete Payment Dossier from all stage outcomes and routes it to the human approval gate.",
        WhenToUse:    "Call after KYC screening (Stage 4). Assembles all stage results into a reviewable dossier. Triggers human approval gate via [RequiresApproval].",
        WhenNotToUse: "Do not call if any prior stage has not PASSED — dossier cannot be submitted with incomplete stage data (spec §4 Stage 5 barrier).",
        Examples:     ["Compile dossier and submit for Finance Approver sign-off for payment PRID-XYZ"],
        InputSchema:  BuildJsonSchema<CompileApprovalDossierInput>(),
        OutputSchema: BuildJsonSchema<CompileApprovalDossierOutput>());

    protected override async Task<ToolResponse<CompileApprovalDossierOutput>> HandleAsync(
        ToolRequest<CompileApprovalDossierInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return ToolResponse<CompileApprovalDossierOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound($"PaymentInstruction '{inp.PaymentId}' not found."));

        // ── Barrier: dossier incomplete ───────────────────────────────────────
        if (payment.CurrentStage < 4)
            return ToolResponse<CompileApprovalDossierOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation(
                    $"Dossier incomplete — payment is at Stage {payment.CurrentStage}. " +
                    "All stages 0-4 must complete before dossier can be submitted for approval."));

        // ── Barrier: tax review hold — WHT determination is unresolved ────────
        // CurrentStage = 3 passes the stage < 4 guard above but the payment is
        // held for manual tax team review; compiling a dossier with null WHT data
        // would produce a misleading and invalid approval package.
        if (payment.Status == PaymentStatus.HeldTaxReview)
            return ToolResponse<CompileApprovalDossierOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation(
                    "Dossier cannot be compiled — payment is held for manual tax review (WHT_REVIEW_REQUIRED). " +
                    "Resume the pipeline after the tax team provides a WHT determination."));

        // ── Barrier: confirmed KYC block prevents approval submission ─────────
        if (payment.Status == PaymentStatus.BlockedKyc)
            return ToolResponse<CompileApprovalDossierOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation(
                    "CONFIRMED KYC block in dossier. Approval cannot be submitted. " +
                    "Dual authorisation from Compliance + Legal required to unblock."));

        // ── Determine approval tier (spec §4 Stage 5) ─────────────────────────
        var netPayable   = payment.NetPayableAmount ?? payment.GrossAmount;
        var approvalTier = netPayable switch
        {
            < 10_000m    => "AUTO",
            < 100_000m   => "SINGLE_FINANCE_APPROVER",
            < 500_000m   => "FINANCE_APPROVER_AND_CFO",
            _            => "FINANCE_APPROVER_CFO_AND_BOARD",
        };

        var kycClear   = payment.KycResult == KycMatchResult.NoMatch;
        var whtFlagged = payment.WhtConfidence == WhtConfidenceLevel.Medium;

        var dossierSummary =
            $"PRID: {payment.Id} | " +
            $"Payer: {payment.PayerName} ({payment.PayerJurisdiction}) | " +
            $"Gross: {payment.GrossAmount} {payment.Currency} | " +
            $"WHT: {payment.WhtAmount ?? 0m} {payment.Currency} ({payment.WhtRate ?? 0m}%) | " +
            $"Net Payable: {netPayable} {payment.Currency} | " +
            $"Service: {payment.ServiceType} | KYC: {payment.KycResult} | " +
            $"WHT Confidence: {payment.WhtConfidence} | Approval Tier: {approvalTier}";

        // For AUTO tier (< $10k, all GREEN), this handler still goes through
        // [RequiresApproval] — the ApprovalBehavior can be enhanced to auto-approve
        // when tier = AUTO and no flags. For POC, all tiers suspend and await sign-off.

        return ToolResponse<CompileApprovalDossierOutput>.Ok(
            request.CorrelationId,
            new CompileApprovalDossierOutput(
                PaymentId:      payment.Id,
                ApprovalTier:   approvalTier,
                DossierSummary: dossierSummary,
                KycClear:       kycClear,
                WhtFlagged:     whtFlagged,
                Message:        $"Dossier compiled. Routed to {approvalTier} for sign-off. " +
                                (whtFlagged ? "⚠ WHT confidence is MEDIUM — flag for Finance Approver attention. " : "") +
                                "Approval SLA: 48 business hours."));
    }
}
