using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Application.Commands;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record ResumeVerifyInput(
    Guid    PaymentId,
    decimal ConfirmedAmount,
    string  ConfirmedCurrency);

public sealed record ResumeVerifyOutput(
    string         VerificationToken,
    Guid           PaymentId,
    string         PayerName,
    string         PayeeRef,
    decimal        GrossAmount,
    string         Currency,
    DateTimeOffset ExpiresAt,
    string         Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// payment.resume-verify — Human confirmation gate before payment execution.
///
/// Verifies three things before issuing a token:
///   1. The payment exists and has passed the approval gate.
///   2. The human operator has approved the payment in the Approvals panel.
///   3. The operator explicitly confirms the PRID, gross amount, and currency.
///
/// Returns a short-lived HMAC token (~10 min) that payment.resume requires.
/// This creates a hard technical dependency — payment.resume cannot proceed
/// without a valid token from this tool.
/// </summary>
public sealed class ResumeVerifyTool
    : DatabaseToolBase<ResumeVerifyInput, ResumeVerifyOutput>
{
    private readonly AppDbContext _db;
    private readonly string       _secret;

    public ResumeVerifyTool(
        IUnitOfWork                         unitOfWork,
        AppDbContext                        db,
        IOptions<PaymentApplicationOptions> options)
        : base(unitOfWork)
    {
        _db     = db;
        _secret = options.Value.ResumeVerificationSecret;
    }

    public override string    Namespace => "payment";
    public override string    Name      => "resume-payment-verify";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Payment verification gate before resuming payment execution. The operator confirms the PRID, gross amount, and currency of a human-approved payment. Returns a short-lived token (~10 min) required by payment.resume — without it resume cannot proceed.",
        WhenToUse:    "Always call before payment.resume. Ask the user to confirm: (1) the PRID, (2) the gross amount as originally initiated, (3) the currency. Human approval must already be granted (check Approvals panel) before calling this. Pass the returned verificationToken to payment.resume.",
        WhenNotToUse: "Do not call if human approval has not yet been granted — direct the user to the Approvals panel first. Do not call if the payment is already SETTLED or FAILED.",
        Examples:     ["Verify GBP 5000 payment to Acme Consulting before execution", "Confirm payment details before resuming USD 10000 transaction"],
        InputSchema:  BuildJsonSchema<ResumeVerifyInput>(),
        OutputSchema: BuildJsonSchema<ResumeVerifyOutput>());

    protected override async Task<ToolResponse<ResumeVerifyOutput>> HandleAsync(
        ToolRequest<ResumeVerifyInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── Load payment ──────────────────────────────────────────────────────
        var payment = await _db.Set<PaymentInstruction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return Fail(request, $"No payment found with PRID '{inp.PaymentId}'. Ensure payment.initiate was called first.");

        // ── Payment must be at the approval gate ──────────────────────────────
        if (payment.Status != PaymentStatus.PendingApproval
         && payment.Status != PaymentStatus.ApprovalGranted)
            return Fail(request,
                $"Payment '{inp.PaymentId}' has status '{payment.Status}'. " +
                "Only payments suspended at the approval gate (Stage 5) can be verified for resume.");

        // ── Human approval must be granted ────────────────────────────────────
        if (!payment.PendingApprovalId.HasValue)
            return Fail(request, "This payment has no associated approval request. Run the pipeline through Stage 5 first.");

        var approval = await _db.Set<ToolEngine.Core.Domain.Entities.PendingApproval>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == payment.PendingApprovalId.Value, ct);

        if (approval is null || approval.Status != ToolEngine.Core.Domain.Enums.ApprovalStatus.Approved)
            return Fail(request,
                $"Human approval has not been granted yet. Current approval status: " +
                $"{approval?.Status.ToString() ?? "NOT_FOUND"}. " +
                "Please approve the payment in the Approvals panel before verifying for resume.");

        // ── Confirm amount ────────────────────────────────────────────────────
        if (Math.Abs(inp.ConfirmedAmount - payment.GrossAmount) > 0.01m)
            return Fail(request,
                $"Amount mismatch: you confirmed {inp.ConfirmedAmount} but the payment gross amount is {payment.GrossAmount} {payment.Currency}. " +
                "Please confirm the exact gross amount as originally initiated.");

        // ── Confirm currency ──────────────────────────────────────────────────
        if (!string.Equals(inp.ConfirmedCurrency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            return Fail(request,
                $"Currency mismatch: you confirmed '{inp.ConfirmedCurrency}' but the payment currency is '{payment.Currency}'. " +
                "Please confirm the correct currency.");

        // ── All checks passed — generate token ────────────────────────────────
        var token     = VerificationTokenHelper.Generate(inp.PaymentId, payment.GrossAmount, payment.Currency, _secret);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

        return ToolResponse<ResumeVerifyOutput>.Ok(
            request.CorrelationId,
            new ResumeVerifyOutput(
                VerificationToken: token,
                PaymentId:         payment.Id,
                PayerName:         payment.PayerName,
                PayeeRef:          payment.PayeeRef,
                GrossAmount:       payment.GrossAmount,
                Currency:          payment.Currency,
                ExpiresAt:         expiresAt,
                Message:           $"Verification passed. {payment.GrossAmount:F2} {payment.Currency} to '{payment.PayeeRef}' confirmed. " +
                                   $"Token valid until ~{expiresAt:HH:mm} UTC. Pass verificationToken to payment.resume now."));
    }

    private static ToolResponse<ResumeVerifyOutput> Fail(
        ToolRequest<ResumeVerifyInput> r, string msg) =>
        ToolResponse<ResumeVerifyOutput>.Fail(r.CorrelationId, ToolError.Validation(msg));
}
