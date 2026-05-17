namespace ToolEngine.Api.Endpoints;

using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Invocation status polling endpoint.
/// Clients call this after receiving HTTP 202 to check whether an
/// approval-suspended tool execution has been approved, denied, or is still pending.
///
/// GET /invocations/{id}/status
/// </summary>
public static class InvocationEndpoints
{
    public static WebApplication MapInvocationEndpoints(this WebApplication app)
    {
        app.MapGet("/invocations/{id:guid}/status", GetStatus)
           .WithTags("Invocations")
           .WithName("GetInvocationStatus")
           .WithSummary("Poll the status of a suspended tool invocation.")
           .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetStatus(
        Guid                                   id,
        HttpContext                             ctx,
        IReadRepository<PendingApproval, Guid> repo,
        CancellationToken                      ct)
    {
        var approval = await repo.GetByIdAsync(id, ct);
        if (approval is null)
            return Results.NotFound(new { error = $"Invocation '{id}' not found." });

        // Tenant isolation.
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        if (!approval.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
            return Results.Forbid();

        var status = approval.IsExpired ? "expired" : approval.Status switch
        {
            ApprovalStatus.Pending  => "pending",
            ApprovalStatus.Approved => "approved",
            ApprovalStatus.Denied   => "denied",
            ApprovalStatus.Expired  => "expired",
            _                       => "unknown"
        };

        return Results.Ok(new
        {
            invocationId    = approval.Id,
            toolFullName    = approval.ToolFullName,
            status,
            risk            = approval.Risk.ToString(),
            channel         = approval.Channel.ToString(),
            createdAt       = approval.CreatedAt,
            expiresAt       = approval.ExpiresAt,
            decidedAt       = approval.DecidedAt,
            decidedByUserId = approval.DecidedByUserId,
            // Populated once the approved execution completes (future resume path).
            result          = approval.SerializedResult is not null
                ? System.Text.Json.JsonSerializer.Deserialize<object>(approval.SerializedResult)
                : null
        });
    }
}
