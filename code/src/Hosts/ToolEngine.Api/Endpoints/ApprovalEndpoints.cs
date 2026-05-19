namespace ToolEngine.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Approval;

/// <summary>
/// Approval management endpoints.
///
/// POST /approvals/{token}/decide  — magic-link approve/deny (token is the shared secret)
/// POST /approvals/otp/verify      — OTP verification for Critical-risk tools
/// GET  /approvals/pending         — list pending approvals for the authenticated approver
/// </summary>
public static class ApprovalEndpoints
{
    /// <summary>
    /// Applied to /approvals/otp/verify when the caller supplies no ApproverUserId.
    /// Identifies OTP-verified approvals in the audit trail without requiring the
    /// caller to claim a user identity they may not have (e.g. CLI flows).
    /// </summary>
    private const string OtpVerifiedApproverIdentity = "otp-verified";

    public static WebApplication MapApprovalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/approvals")
                       .WithTags("Approvals");

        // Magic-link: token in URL acts as the shared secret — no JWT required.
        group.MapPost("/{token}/decide", Decide)
             .WithName("DecideApproval")
             .WithSummary("Approve or deny a pending tool invocation via magic-link token.");

        // OTP path: 6-digit code sent to approver's email for Critical-risk tools.
        // Rate-limited per IP (10 attempts / 10 min). Entity-level counter handles
        // per-token lockout after 5 failures (OWASP MFA Cheat Sheet).
        group.MapPost("/otp/verify", VerifyOtp)
             .WithName("VerifyApprovalOtp")
             .WithSummary("Verify an OTP to approve a pending tool invocation.")
             .RequireRateLimiting(RateLimitPolicies.OtpVerify);

        // Dashboard: authenticated approver views pending approvals for their tenant.
        group.MapGet("/pending", GetPending)
             .WithName("GetPendingApprovals")
             .WithSummary("List pending approvals visible to the authenticated user.")
             .RequireAuthorization();

        return app;
    }

    // POST /approvals/{token}/decide?action=approve|deny
    private static async Task<IResult> Decide(
        string                             token,
        [FromQuery] string                 action,
        [FromBody]  DecideRequest?         body,
        IRepository<PendingApproval, Guid> repo,
        IUnitOfWork                        uow,
        IReadRepository<PendingApproval, Guid> readRepo,
        CancellationToken                  ct)
    {
        var approval = await FindByTokenAsync(readRepo, token, ct);
        if (approval is null)
            return Results.Problem("Invalid or expired approval token.", statusCode: 404,
                title: "NOT_FOUND");

        // Re-fetch via write repo to get tracked entity.
        var tracked = await repo.GetByIdAsync(approval.Id, ct);
        if (tracked is null)
            return Results.Problem("Approval not found.", statusCode: 404, title: "NOT_FOUND");

        if (tracked.IsExpired)
        {
            tracked.Expire();
            await uow.SaveChangesAsync(ct);
            return Results.Problem("Approval request has expired.", statusCode: 410,
                title: ErrorCodes.ApprovalExpired);
        }

        // The magic-link token is the shared secret; the caller's identity is always "magic-link".
        // Accepting DecidedByUserId from the request body would allow anyone with the URL to forge
        // arbitrary audit identities — the token alone authorises the decision.
        var decidedBy = "magic-link";

        var result = action.ToLowerInvariant() switch
        {
            "approve" => tracked.Approve(decidedBy),
            "deny"    => tracked.Deny(decidedBy, body?.Reason),
            _         => null
        };

        if (result is null)
            return Results.BadRequest(new { error = "action must be 'approve' or 'deny'." });

        if (result.IsFailure)
            return Results.Conflict(new { error = result.Error.Description });

        await uow.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            invocationId = tracked.Id,
            status       = tracked.Status.ToString(),
            decidedBy,
            decidedAt    = tracked.DecidedAt
        });
    }

    // POST /approvals/otp/verify
    private static async Task<IResult> VerifyOtp(
        [FromBody]  OtpVerifyRequest           body,
        IRepository<PendingApproval, Guid>     repo,
        IReadRepository<PendingApproval, Guid> readRepo,
        IUnitOfWork                            uow,
        CancellationToken                      ct)
    {
        var approval = await FindByTokenAsync(readRepo, body.ApprovalToken, ct);
        if (approval is null)
            return Results.Problem("Invalid or expired approval token.", statusCode: 404,
                title: ErrorCodes.InvalidApprovalToken);

        var tracked = await repo.GetByIdAsync(approval.Id, ct);
        if (tracked is null)
            return Results.Problem("Approval not found.", statusCode: 404, title: "NOT_FOUND");

        if (tracked.Channel != ApprovalChannelType.EmailOtp)
            return Results.BadRequest(new { error = "This approval does not use OTP verification." });

        if (tracked.IsExpired)
        {
            tracked.Expire();
            await uow.SaveChangesAsync(ct);
            return Results.Problem("Approval request has expired.", statusCode: 410,
                title: ErrorCodes.ApprovalExpired);
        }

        // VerifyOtp re-derives PBKDF2 with the embedded salt and uses
        // CryptographicOperations.FixedTimeEquals to prevent timing-oracle attacks.
        // The fixed-time comparison ensures an attacker cannot distinguish a wrong OTP
        // from a wrong key-derivation parameter by measuring response latency.
        if (tracked.OtpHash is null ||
            !EmailOtpChannel.VerifyOtp(body.Otp, tracked.OtpHash))
        {
            // Increment failure counter. IncrementFailedOtpAttempts returns true
            // when the max is reached and transitions the approval to Expired.
            var locked = tracked.IncrementFailedOtpAttempts(ServiceLimits.OtpMaxFailedAttempts);
            await uow.SaveChangesAsync(ct);

            return locked
                ? Results.Problem(
                    "Maximum OTP attempts exceeded. Approval request has been invalidated.",
                    statusCode: 410, title: ErrorCodes.ApprovalExpired)
                : Results.Problem(
                    $"Invalid OTP. {ServiceLimits.OtpMaxFailedAttempts - tracked.FailedOtpAttempts} attempt(s) remaining.",
                    statusCode: 400, title: ErrorCodes.InvalidOtp);
        }

        var result = tracked.Approve(body.ApproverUserId ?? OtpVerifiedApproverIdentity);
        if (result.IsFailure)
            return Results.Conflict(new { error = result.Error.Description });

        await uow.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            invocationId = tracked.Id,
            status       = tracked.Status.ToString(),
            decidedAt    = tracked.DecidedAt
        });
    }

    // GET /approvals/pending
    private static async Task<IResult> GetPending(
        HttpContext                            ctx,
        IReadRepository<PendingApproval, Guid> repo,
        CancellationToken                      ct)
    {
        var tenantId = ctx.User.FindFirst(JwtClaimNames.TenantId)?.Value ?? "anonymous";

        var spec = new LambdaSpecification<PendingApproval>(
            a => a.TenantId == tenantId && a.Status == ApprovalStatus.Pending);

        var all = await repo.ListAsync(spec, ct);

        var result = all
            .Where(a => !a.IsExpired)
            .Select(a => new
            {
                invocationId = a.Id,
                toolFullName = a.ToolFullName,
                risk         = a.Risk.ToString(),
                reason       = a.ApprovalReason,
                requestedBy  = a.UserId,
                channel      = a.Channel.ToString(),
                expiresAt    = a.ExpiresAt,
                createdAt    = a.CreatedAt
            });

        return Results.Ok(result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<PendingApproval?> FindByTokenAsync(
        IReadRepository<PendingApproval, Guid> repo,
        string                                 token,
        CancellationToken                      ct)
    {
        var spec = new LambdaSpecification<PendingApproval>(
            a => a.ApprovalToken == token);
        var results = await repo.ListAsync(spec, ct);
        return results.FirstOrDefault();
    }

    private sealed record DecideRequest(
        string? DecidedByUserId = null,
        string? Reason          = null);

    private sealed record OtpVerifyRequest(
        string  ApprovalToken,
        string  Otp,
        string? ApproverUserId = null);
}
