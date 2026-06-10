using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Application.Commands;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record ResumePaymentToolInput(
    Guid   PaymentId,
    string VerificationToken);

public sealed record ResumePaymentToolOutput(
    string  Status,
    string? BankTransactionId,
    string  Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// payment.resume — Executes Stage 6 (bank submission) and Stage 7 (reconciliation)
/// after human approval and operator verification.
///
/// Hard dependency on payment.resume-payment-verify: the verificationToken returned by
/// that tool is required here. An invalid or expired token is rejected with a
/// clear message directing the operator to re-verify.
/// Applies to ALL callers — LLM agents, direct API, CLI.
/// </summary>
public sealed class ResumePaymentTool
    : DatabaseToolBase<ResumePaymentToolInput, ResumePaymentToolOutput>
{
    private readonly AppDbContext _db;
    private readonly ISender      _mediator;
    private readonly string       _secret;

    public ResumePaymentTool(
        IUnitOfWork                         unitOfWork,
        AppDbContext                        db,
        ISender                             mediator,
        IOptions<PaymentApplicationOptions> options)
        : base(unitOfWork)
    {
        _db       = db;
        _mediator = mediator;
        _secret   = options.Value.ResumeVerificationSecret;
    }

    public override string    Namespace => "payment";
    public override string    Name      => "resume";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Executes bank submission (Stage 6) and reconciliation (Stage 7) for a human-approved payment. Requires a valid `verificationToken` — a short-lived HMAC token obtained after full payment re-validation.",
        WhenToUse:    "Requires `paymentId` and a valid `verificationToken` (obtained from payment.resume-payment-verify). On success returns SETTLED with a bankTransactionId.",
        WhenNotToUse: "Do not call without a valid verificationToken. Do not call if the payment is not in an approved state. Do not call if the payment is already SETTLED or FAILED.",
        Examples:     ["Resume approved GBP 5000 payment after verification", "Execute Stage 6-7 after human approval and verification"],
        InputSchema:  BuildJsonSchema<ResumePaymentToolInput>(),
        OutputSchema: BuildJsonSchema<ResumePaymentToolOutput>());

    protected override async Task<ToolResponse<ResumePaymentToolOutput>> HandleAsync(
        ToolRequest<ResumePaymentToolInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── Load payment to get the authoritative amount + currency for token validation ─
        var payment = await _db.Set<PaymentInstruction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return Fail(request, $"No payment found with PRID '{inp.PaymentId}'.");

        // ── Validate verification token ────────────────────────────────────────
        // Recomputes expected token for the current and previous 5-minute windows.
        // Uses the authoritative stored values — cannot be forged without the secret.
        var tokenValid = VerificationTokenHelper.Validate(
            inp.VerificationToken,
            inp.PaymentId,
            payment.GrossAmount,
            payment.Currency,
            _secret);

        if (!tokenValid)
            return Fail(request,
                "Verification token is invalid or has expired (~10 minute window). " +
                "Call payment.resume-payment-verify again to obtain a fresh token, then retry.");

        // ── Delegate to ResumePaymentCommand (Stages 6-7) ─────────────────────
        var result = await _mediator.Send(
            new ResumePaymentCommand(inp.PaymentId, "agent-user"), ct);

        if (!result.IsSuccess)
            return Fail(request, result.ErrorCode is not null
                ? $"{result.ErrorCode}: {result.Message}"
                : result.Message ?? "Payment resume failed.");

        return ToolResponse<ResumePaymentToolOutput>.Ok(
            request.CorrelationId,
            new ResumePaymentToolOutput(
                Status:            result.Status,
                BankTransactionId: result.BankTransactionId,
                Message:           result.Message
                    ?? $"Payment settled successfully. Bank transaction ID: {result.BankTransactionId}"));
    }

    private static ToolResponse<ResumePaymentToolOutput> Fail(
        ToolRequest<ResumePaymentToolInput> r, string msg) =>
        ToolResponse<ResumePaymentToolOutput>.Fail(r.CorrelationId, ToolError.Validation(msg));
}
