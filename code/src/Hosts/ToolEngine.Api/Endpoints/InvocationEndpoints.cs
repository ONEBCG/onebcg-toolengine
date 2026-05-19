namespace ToolEngine.Api.Endpoints;

using System.Text.Json;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Constants;
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

        // Tenant isolation — a tenant must not be able to poll another tenant's approvals.
        var tenantId = ctx.User.FindFirst(JwtClaimNames.TenantId)?.Value ?? "anonymous";
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
            // TryDeserializeResult prevents a malformed stored JSON blob from returning 500
            // and permanently breaking status polling for this invocation.
            result          = approval.SerializedResult is not null
                ? TryDeserializeResult(approval.SerializedResult)
                : null
        });
    }

    /// <summary>
    /// Safe deserialisation of a stored tool result JSON string.
    /// Returns null (with no exception) when the stored value is malformed so that
    /// a corrupt result row does not break status polling indefinitely.
    /// </summary>
    private static object? TryDeserializeResult(string json)
    {
        try   { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return null; }
    }
}
