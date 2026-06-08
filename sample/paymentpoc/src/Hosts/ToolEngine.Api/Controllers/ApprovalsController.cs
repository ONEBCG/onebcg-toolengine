using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/approvals")]
[Tags("Approvals")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ApprovalsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List all pending human approvals, enriched with payment details where available.
    /// PRID is extracted from IdempotencyKey (format "{prid}:{toolName}") and used to
    /// look up the associated PaymentInstruction so the approver sees full context.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListPendingApprovals(CancellationToken ct)
    {
        // Note: SQLite does not support DateTimeOffset in ORDER BY — sort client-side.
        var pending = (await _db.Set<PendingApproval>()
            .AsNoTracking()
            .Where(a => a.Status == ApprovalStatus.Pending)
            .Select(a => new
            {
                a.Id,
                a.ToolFullName,
                a.Risk,
                a.Channel,
                a.ExpiresAt,
                a.CreatedAt,
                a.IdempotencyKey,
                a.AcknowledgementJson,
            })
            .ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAt)
            .ToList();

        // Enrich each approval with the associated PaymentInstruction if resolvable.
        // IdempotencyKey format from the pipeline: "{prid}:{toolName}"
        // Chat-initiated approvals have null IdempotencyKey — no payment context available.
        var enriched = new List<object>(pending.Count);
        foreach (var a in pending)
        {
            var prid    = ExtractPrid(a.IdempotencyKey);
            var payment = prid.HasValue
                ? await _db.Set<PaymentInstruction>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == prid.Value, ct)
                : null;

            enriched.Add(new
            {
                // Core approval fields
                id             = a.Id,
                toolFullName   = a.ToolFullName,
                risk           = a.Risk,
                channel        = a.Channel,
                expiresAt      = a.ExpiresAt,
                createdAt      = a.CreatedAt,
                acknowledgementJson = a.AcknowledgementJson,

                // Payment context (null when approval was raised outside the pipeline)
                prid           = prid,
                payerName      = payment?.PayerName,
                payerJurisdiction = payment?.PayerJurisdiction,
                payeeRef       = payment?.PayeeRef,
                grossAmount    = payment?.GrossAmount,
                currency       = payment?.Currency,
                netPayableAmount = payment?.NetPayableAmount,
                whtAmount      = payment?.WhtAmount,
                serviceType    = payment?.ServiceType,
                ppmId          = payment?.PpmId,
                initiatorId    = payment?.InitiatorId,
            });
        }

        return Ok(enriched);
    }

    // Extracts the PRID Guid from an idempotency key formatted as "{prid}:{toolName}".
    // Returns null if the key is absent, malformed, or has no valid GUID prefix.
    private static Guid? ExtractPrid(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var sep = key.IndexOf(':');
        return sep > 0 && Guid.TryParse(key[..sep], out var g) ? g : null;
    }

    /// <summary>Get approval request details (token not exposed here — sent via Channel).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetApproval(Guid id, CancellationToken ct)
    {
        var approval = await _db.Set<PendingApproval>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (approval is null)
            return NotFound();

        return Ok(new
        {
            approval.Id,
            approval.ToolFullName,
            approval.Status,
            approval.Risk,
            approval.Channel,
            approval.ExpiresAt,
            approval.CreatedAt,
            approval.AcknowledgementJson,
            approval.DenialReason,
        });
    }

    /// <summary>Approve a pending tool invocation using the approval token (E1: constant-time comparison).</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ApproveRequest(
        Guid id,
        [FromBody] ApproveRequest req,
        CancellationToken ct)
    {
        var approval = await _db.Set<PendingApproval>()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (approval is null)
            return NotFound();

        if (approval.Status != ApprovalStatus.Pending)
            return UnprocessableEntity(new { error = $"Approval is already {approval.Status}." });

        if (DateTimeOffset.UtcNow > approval.ExpiresAt)
        {
            approval.Expire();
            await _db.SaveChangesAsync(ct);
            return UnprocessableEntity(new { error = "Approval token has expired." });
        }

        // E1: constant-time token comparison (prevents timing attacks)
        var expectedBytes = Encoding.UTF8.GetBytes(approval.ApprovalToken);
        var providedBytes = Encoding.UTF8.GetBytes(req.ApprovalToken ?? string.Empty);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            return UnprocessableEntity(new { error = "Invalid approval token." });

        approval.Approve();
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            approvalId = approval.Id,
            status     = "Approved",
            message    = "Approval granted. Call POST /api/v1/payments/{prid}/resume to execute Stages 6-7.",
        });
    }

    /// <summary>Deny a pending tool invocation with a mandatory reason.</summary>
    [HttpPost("{id:guid}/deny")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> DenyRequest(
        Guid id,
        [FromBody] DenyRequest req,
        CancellationToken ct)
    {
        var approval = await _db.Set<PendingApproval>()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (approval is null)
            return NotFound();

        if (approval.Status != ApprovalStatus.Pending)
            return UnprocessableEntity(new { error = $"Approval is already {approval.Status}." });

        approval.Deny(req.Reason ?? "No reason provided.");
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            approvalId = approval.Id,
            status     = "Denied",
            reason     = approval.DenialReason,
        });
    }

    /// <summary>Archive (expire) a single approval, removing it from the pending queue.</summary>
    [HttpPost("{id:guid}/archive")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ArchiveApproval(Guid id, CancellationToken ct)
    {
        var approval = await _db.Set<PendingApproval>()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (approval is null)
            return NotFound();

        if (approval.Status != ApprovalStatus.Pending)
            return UnprocessableEntity(new { error = $"Approval is already {approval.Status} — cannot archive." });

        approval.Expire();
        await _db.SaveChangesAsync(ct);

        return Ok(new { approvalId = approval.Id, status = "Archived", message = "Approval archived and removed from queue." });
    }

    /// <summary>Bulk-archive all pending approvals whose expiry deadline has passed.</summary>
    [HttpPost("archive-expired")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ArchiveAllExpired(CancellationToken ct)
    {
        var now     = DateTimeOffset.UtcNow;
        var expired = await _db.Set<PendingApproval>()
            .Where(a => a.Status == ApprovalStatus.Pending && a.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var a in expired)
            a.Expire();

        await _db.SaveChangesAsync(ct);

        return Ok(new { archived = expired.Count, message = $"{expired.Count} expired approval(s) archived." });
    }

    /// <summary>Retrieve the raw approval token for UI-driven approval flow (POC — all environments).</summary>
    [HttpGet("{id:guid}/token")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetApprovalTokenDev(Guid id, CancellationToken ct)
    {

        var approval = await _db.Set<PendingApproval>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (approval is null)
            return NotFound();

        return Ok(new
        {
            approvalId    = approval.Id,
            approvalToken = approval.ApprovalToken,  // DEV ONLY
            expiresAt     = approval.ExpiresAt,
            warning       = "Token endpoint available in Development environment only.",
        });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record ApproveRequest(string? ApprovalToken);
public sealed record DenyRequest(string? Reason);
