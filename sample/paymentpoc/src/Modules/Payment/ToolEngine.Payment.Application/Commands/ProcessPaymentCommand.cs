using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Payment.Tools.Stage0_Initiate;
using ToolEngine.Payment.Tools.Stage1_VerifyPayee;
using ToolEngine.Payment.Tools.Stage2_PpmCheck;
using ToolEngine.Payment.Tools.Stage3_CalculateWht;
using ToolEngine.Payment.Tools.Stage4_KycScreen;
using ToolEngine.Payment.Tools.Stage5_CompileDossier;

namespace ToolEngine.Payment.Application.Commands;

// ── Command ───────────────────────────────────────────────────────────────────

public sealed record ProcessPaymentCommand(
    string      PayerName,
    string      PayerJurisdiction,
    string      PayerEntityId,
    string      PayeeRef,
    decimal     GrossAmount,
    string      Currency,
    ServiceType ServiceType,
    string      PpmId,
    string      InitiatorId,
    CallerType  CallerType = CallerType.Human) : IRequest<ProcessPaymentResult>;

// ── Result ────────────────────────────────────────────────────────────────────

public sealed record ProcessPaymentResult(
    bool   IsSuccess,
    Guid?  Prid,
    string Status,
    string Message,
    Guid?  PendingApprovalId = null,
    string? ApprovalTier     = null,
    string? ErrorCode        = null,
    int    StageReached      = 0);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates Stages 0–5 of the payment pipeline.
/// Each stage is executed via MediatR (ExecuteToolCommand) so all 7 MediatR
/// pipeline behaviors (Validation → Budget → Loop → Approval → Audit) fire per stage.
///
/// Stage 5 (compile-dossier) has [RequiresApproval(High)] — ApprovalBehavior suspends
/// and returns IsSuspended = true. Handler detects suspension, persists PendingApprovalId
/// on the PaymentInstruction, and returns 202.
///
/// After approval: call ResumePaymentCommandHandler to execute Stages 6–7.
/// </summary>
public sealed class ProcessPaymentCommandHandler
    : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>
{
    private readonly ISender      _mediator;
    private readonly AppDbContext _db;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Must match ToolExecutor._jsonOptions — enums serialise as strings there,
        // so they must also deserialise as strings when unwrapping stage outputs.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public ProcessPaymentCommandHandler(ISender mediator, AppDbContext db)
    {
        _mediator = mediator;
        _db       = db;
    }

    public async Task<ProcessPaymentResult> Handle(
        ProcessPaymentCommand cmd, CancellationToken ct)
    {
        // ── Stage 0: Initiate (Database — validates and creates PaymentInstruction) ──
        // InitiatePaymentHandler now persists the record and returns the real PRID.
        // No separate creation step needed here.
        var (initOk, initData, initErr) = await RunStageAsync<InitiatePaymentOutput>(
            PaymentPipeline.Namespace, PaymentPipeline.Stage.Initiate, PaymentPipeline.Version,
            new InitiatePaymentInput(
                cmd.PayerName, cmd.PayerJurisdiction, cmd.PayerEntityId,
                cmd.PayeeRef, cmd.GrossAmount, cmd.Currency,
                cmd.ServiceType, cmd.PpmId, cmd.InitiatorId),
            Guid.NewGuid(), cmd.CallerType,
            idempotencyKey: null, ct);

        if (!initOk)
            return Fail(null, 0, initErr);

        var prid = initData!.Prid;

        await LogAuditAsync(prid, 0, "Initiation", "PASS",
            $"Payment instruction created. PRID: {prid}", cmd.InitiatorId, ct);

        // ── Stage 1: Verify Payee (Database) ─────────────────────────────────
        var (payeeOk, payeeData, payeeErr) = await RunStageAsync<VerifyPayeeOutput>(
            PaymentPipeline.Namespace, PaymentPipeline.Stage.VerifyPayee, PaymentPipeline.Version,
            new VerifyPayeeInput(prid, cmd.PayeeRef),
            Guid.NewGuid(), cmd.CallerType,
            idempotencyKey: $"{prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.VerifyPayee}", ct);

        await LogAuditAsync(prid, 1, "PayeeVerification",
            payeeOk ? "PASS" : "BLOCKED",
            payeeOk ? $"Payee verified: {payeeData?.LegalName}" : payeeErr,
            cmd.InitiatorId, ct);

        if (!payeeOk)
            return Fail(prid, 1, payeeErr);

        var verifiedPayeeId = payeeData!.PayeeId;

        // ── Stage 2: PPM Check (Database) ─────────────────────────────────────
        var (ppmOk, ppmData, ppmErr) = await RunStageAsync<PpmCheckOutput>(
            PaymentPipeline.Namespace, PaymentPipeline.Stage.PpmCheck, PaymentPipeline.Version,
            new PpmCheckInput(prid, cmd.PpmId, verifiedPayeeId,
                cmd.ServiceType, cmd.GrossAmount, cmd.Currency),
            Guid.NewGuid(), cmd.CallerType,
            idempotencyKey: $"{prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.PpmCheck}", ct);

        await LogAuditAsync(prid, 2, "ContractCheck",
            ppmOk ? "PASS" : "BLOCKED",
            ppmOk ? $"PPM permitted. Contract: {ppmData?.PpmId}" : ppmErr,
            cmd.InitiatorId, ct);

        if (!ppmOk)
            return Fail(prid, 2, ppmErr);

        // ── Stage 3: WHT Calculation (Logic — stub returns 0%) ────────────────
        var payerJurisdiction = cmd.PayerJurisdiction;
        var payeeRecord = await _db.Set<PayeeRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == verifiedPayeeId, ct);

        var (whtOk, whtData, whtErr) = await RunStageAsync<CalculateWhtOutput>(
            PaymentPipeline.Namespace, PaymentPipeline.Stage.CalculateWht, PaymentPipeline.Version,
            new CalculateWhtInput(
                prid, payerJurisdiction,
                payeeRecord?.Jurisdiction ?? "UNKNOWN",
                cmd.ServiceType, cmd.GrossAmount, cmd.Currency,
                DateTimeOffset.UtcNow.Year),
            Guid.NewGuid(), cmd.CallerType,
            idempotencyKey: $"{prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.CalculateWht}", ct);

        if (whtOk && whtData is not null)
        {
            // Reload instruction (Stage 1 handler may have mutated it)
            var freshInstr = await _db.Set<PaymentInstruction>()
                .FirstOrDefaultAsync(p => p.Id == prid, ct);
            freshInstr?.ApplyWhtCalculation(
                whtData.WhtRatePct, whtData.WhtAmount, whtData.NetPayableAmount,
                whtData.ConfidenceLevel, whtData.Justification,
                whtData.ApplicableTreaty, whtData.ServiceClassification);
            await _db.SaveChangesAsync(ct);
        }

        // Differentiate HELD_TAX_REVIEW from PASS — ReviewRequired confidence is a hold,
        // not a pass, and must be reflected accurately in the audit trail.
        var stage3Outcome = !whtOk
            ? "HELD"
            : whtData?.ConfidenceLevel == WhtConfidenceLevel.ReviewRequired
                ? "HELD_TAX_REVIEW"
                : "PASS";

        await LogAuditAsync(prid, 3, "TaxCalculation",
            stage3Outcome, whtOk ? whtData?.Message : whtErr,
            cmd.InitiatorId, ct);

        if (!whtOk)
            return Fail(prid, 3, whtErr);

        // ── Stage 4: KYC Screening (Api — stub returns CONFIRMED_MATCH) ───────
        var (kycOk, kycData, kycErr) = await RunStageAsync<KycScreenOutput>(
            PaymentPipeline.Namespace, PaymentPipeline.Stage.KycScreen, PaymentPipeline.Version,
            new KycScreenInput(
                prid, verifiedPayeeId,
                payeeRecord?.LegalName ?? cmd.PayeeRef,
                payeeRecord?.Jurisdiction ?? cmd.PayerJurisdiction,
                payeeRecord?.EntityType.ToString() ?? "Corporate",
                payeeRecord?.TaxIdentifier,
                cmd.GrossAmount, cmd.ServiceType.ToString()),
            Guid.NewGuid(), cmd.CallerType,
            idempotencyKey: $"{prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.KycScreen}", ct);

        await LogAuditAsync(prid, 4, "KycScreening",
            kycOk ? "PASS" : "BLOCKED",
            kycOk ? kycData?.Message : kycErr,
            cmd.InitiatorId, ct);

        if (!kycOk)
            return Fail(prid, 4, kycErr);

        // ── Stage 5: Compile Dossier + Approval Gate ──────────────────────────
        // [RequiresApproval(High)] on handler → ApprovalBehavior suspends → HTTP 202
        var s5Response = await _mediator.Send(new ExecuteToolCommand(
            CorrelationId:  Guid.NewGuid(),
            ToolNamespace:  PaymentPipeline.Namespace,
            ToolName:       PaymentPipeline.Stage.CompileDossier,
            ToolVersion:    PaymentPipeline.Version,
            Input:          ToJson(new CompileApprovalDossierInput(prid)),
            UserId:         cmd.InitiatorId,
            CallerType:     cmd.CallerType,
            IdempotencyKey: $"{prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.CompileDossier}"), ct);

        if (s5Response.IsSuspended)
        {
            var pendingId = s5Response.PendingInvocationId;

            // Guard: a suspended response must carry a valid PendingInvocationId.
            // Storing Guid.Empty would permanently break the resume flow.
            if (pendingId is null || pendingId == Guid.Empty)
                return new ProcessPaymentResult(
                    IsSuccess:    false,
                    Prid:         prid,
                    Status:       "UNEXPECTED",
                    Message:      "Stage 5 returned suspended but PendingInvocationId is missing or empty. " +
                                  "Check AsyncApprovalGate and ApprovalBehavior.",
                    ErrorCode:    PaymentErrorCodes.NullApprovalId,
                    StageReached: 5);

            var instr5 = await _db.Set<PaymentInstruction>()
                .FirstOrDefaultAsync(p => p.Id == prid, ct);

            if (instr5 is null)
                return new ProcessPaymentResult(
                    IsSuccess:    false,
                    Prid:         prid,
                    Status:       "UNEXPECTED",
                    Message:      "PaymentInstruction was deleted before Stage 5 could record the approval gate.",
                    ErrorCode:    PaymentErrorCodes.InstructionMissing,
                    StageReached: 5);

            instr5.MarkPendingApproval(pendingId.Value, "PENDING");
            await _db.SaveChangesAsync(ct);

            await LogAuditAsync(prid, 5, "ApprovalGate", "PENDING",
                $"Dossier submitted. PendingApprovalId: {pendingId}",
                cmd.InitiatorId, ct);

            return new ProcessPaymentResult(
                IsSuccess:        false,
                Prid:             prid,
                Status:           "PENDING_APPROVAL",
                Message:          "Stages 0–4 complete. Routed to approval gate. " +
                                  "POST /api/v1/approvals/{id}/approve → then POST /api/v1/payments/{prid}/resume.",
                PendingApprovalId: pendingId,
                ApprovalTier:     "SEE_DOSSIER",
                StageReached:     5);
        }

        // Stage 5 returned without suspension (unexpected in current POC config)
        return new ProcessPaymentResult(
            IsSuccess:   false,
            Prid:        prid,
            Status:      "UNEXPECTED",
            Message:     "Stage 5 returned without approval suspension. Check ApprovalBehavior config.",
            StageReached: 5);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(bool Ok, TOutput? Data, string? Error)> RunStageAsync<TOutput>(
        string ns, string name, string version,
        object input, Guid correlationId, CallerType callerType,
        string? idempotencyKey, CancellationToken ct)
    {
        var response = await _mediator.Send(new ExecuteToolCommand(
            CorrelationId:  correlationId,
            ToolNamespace:  ns,
            ToolName:       name,
            ToolVersion:    version,
            Input:          ToJson(input),
            UserId:         null,
            CallerType:     callerType,
            IdempotencyKey: idempotencyKey), ct);

        if (response is ToolResponse<JsonElement> typed)
        {
            if (typed.Success)
                return (true, typed.Data.Deserialize<TOutput>(_json), null);

            return (false, default, typed.Error?.Description ?? "Unknown error");
        }

        return (false, default, "Unexpected IToolResponse type.");
    }

    private static ProcessPaymentResult Fail(Guid? prid, int stage, string? error) =>
        new(IsSuccess:    false,
            Prid:         prid,
            Status:       "BLOCKED",
            Message:      error ?? "Stage failed.",
            StageReached: stage);

    private static JsonElement ToJson(object obj) =>
        JsonSerializer.SerializeToElement(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    private async Task LogAuditAsync(
        Guid paymentId, int stage, string stageName,
        string outcome, string? details, string? actorId, CancellationToken ct)
    {
        _db.Set<PaymentAuditLog>().Add(
            PaymentAuditLog.Create(
                paymentId, stage, stageName, outcome, details, actorId,
                DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);
    }
}
