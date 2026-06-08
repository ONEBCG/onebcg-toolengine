using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Payment.Application.Commands;
using ToolEngine.Payment.Application.Queries;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
[Tags("Payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _mediator;

    public PaymentsController(ISender mediator) => _mediator = mediator;

    /// <summary>
    /// Initiate a B2B payment through the 7-stage processing pipeline.
    /// Stages 0–5 run synchronously. Stage 5 approval gate returns HTTP 202.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(202)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> InitiatePayment(
        [FromBody] InitiatePaymentRequest req,
        CancellationToken ct)
    {
        // H7: server-side input validation — never trust client-supplied values
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(req.PayerName))           errors.Add("PayerName is required.");
        else if (req.PayerName.Length > 256)                    errors.Add("PayerName must be ≤ 256 characters.");

        if (string.IsNullOrWhiteSpace(req.PayerJurisdiction))   errors.Add("PayerJurisdiction is required.");
        else if (req.PayerJurisdiction.Length > 8)              errors.Add("PayerJurisdiction must be ≤ 8 characters.");

        if (string.IsNullOrWhiteSpace(req.PayerEntityId))       errors.Add("PayerEntityId is required.");
        else if (req.PayerEntityId.Length > 256)                errors.Add("PayerEntityId must be ≤ 256 characters.");

        if (string.IsNullOrWhiteSpace(req.PayeeRef))            errors.Add("PayeeRef is required.");
        else if (req.PayeeRef.Length > 256)                     errors.Add("PayeeRef must be ≤ 256 characters.");

        if (req.GrossAmount <= 0)                               errors.Add("GrossAmount must be greater than zero.");
        else if (req.GrossAmount > 10_000_000)                  errors.Add("GrossAmount must not exceed 10,000,000.");

        if (string.IsNullOrWhiteSpace(req.Currency))            errors.Add("Currency is required.");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(req.Currency, @"^[A-Z]{3}$"))
                                                                errors.Add("Currency must be a 3-letter ISO 4217 code (e.g. USD, SGD).");

        if (!Enum.IsDefined(typeof(ServiceType), req.ServiceType))
                                                                errors.Add($"ServiceType '{req.ServiceType}' is not a recognised value.");

        if (string.IsNullOrWhiteSpace(req.PpmId))              errors.Add("PpmId is required.");
        else if (req.PpmId.Length > 128)                       errors.Add("PpmId must be ≤ 128 characters.");

        if (errors.Count > 0)
            return BadRequest(new { errors });

        var command = new ProcessPaymentCommand(
            PayerName:         req.PayerName,
            PayerJurisdiction: req.PayerJurisdiction,
            PayerEntityId:     req.PayerEntityId,
            PayeeRef:          req.PayeeRef,
            GrossAmount:       req.GrossAmount,
            Currency:          req.Currency,
            ServiceType:       req.ServiceType,
            PpmId:             req.PpmId,
            InitiatorId:       User.FindFirst("sub")?.Value ?? "api-initiator",
            CallerType:        req.CallerType);

        var result = await _mediator.Send(command, ct);

        if (result.Status == "PENDING_APPROVAL")
        {
            var approvalUri = $"/api/v1/approvals/{result.PendingApprovalId}";
            Response.Headers.Append("Location",    approvalUri);
            Response.Headers.Append("Retry-After", "3600");

            return Accepted(approvalUri, new
            {
                prid              = result.Prid,
                status            = result.Status,
                message           = result.Message,
                pendingApprovalId = result.PendingApprovalId,
                approvalTier      = result.ApprovalTier,
                stageReached      = result.StageReached,
                resumeUrl         = result.Prid.HasValue
                    ? $"/api/v1/payments/{result.Prid}/resume"
                    : null,
            });
        }

        if (!result.IsSuccess)
            return UnprocessableEntity(new
            {
                prid         = result.Prid,
                status       = result.Status,
                message      = result.Message,
                errorCode    = result.ErrorCode,
                stageReached = result.StageReached,
            });

        return Ok(result);
    }

    /// <summary>Get current status and key fields of a payment instruction.</summary>
    [HttpGet("{prid:guid}")]
    [ProducesResponseType<PaymentStatusDto>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetPaymentStatus(Guid prid, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPaymentStatusQuery(prid), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Get the full stage-by-stage audit trail for a payment instruction (7-year retention).</summary>
    [HttpGet("{prid:guid}/audit")]
    [ProducesResponseType<IReadOnlyList<PaymentAuditLogDto>>(200)]
    public async Task<IActionResult> GetPaymentAuditTrail(Guid prid, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPaymentAuditTrailQuery(prid), ct);
        return Ok(result);
    }

    /// <summary>
    /// Resume payment execution (Stages 6–7) after approval has been granted.
    /// Validates PendingApproval status before proceeding.
    /// </summary>
    [HttpPost("{prid:guid}/resume")]
    [ProducesResponseType<ResumePaymentResult>(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ResumePayment(Guid prid, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResumePaymentCommand(
            Prid:       prid,
            ApproverId: User.FindFirst("sub")?.Value), ct);

        return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record InitiatePaymentRequest(
    string      PayerName,
    string      PayerJurisdiction,
    string      PayerEntityId,
    string      PayeeRef,
    decimal     GrossAmount,
    string      Currency,
    ServiceType ServiceType,
    string      PpmId,
    CallerType  CallerType = CallerType.Human);
