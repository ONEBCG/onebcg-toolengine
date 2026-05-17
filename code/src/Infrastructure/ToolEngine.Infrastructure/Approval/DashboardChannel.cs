namespace ToolEngine.Infrastructure.Approval;

using Microsoft.Extensions.Logging;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// No-push channel: the PendingApproval record is already in the DB.
/// The approver logs into the dashboard and acts via GET /approvals/pending.
/// Used as the default dev channel and as the silent fallback when no
/// other channel is configured.
/// </summary>
public sealed class DashboardChannel : IApprovalChannel
{
    private readonly ILogger<DashboardChannel> _log;

    public ApprovalChannelType ChannelType => ApprovalChannelType.Dashboard;

    public DashboardChannel(ILogger<DashboardChannel> log) => _log = log;

    public Task SendAsync(PendingApproval approval, CancellationToken ct = default)
    {
        _log.LogInformation(
            "DashboardChannel: approval {Id} for {ToolFullName} is pending. " +
            "Approver must review at GET /approvals/pending.",
            approval.Id, approval.ToolFullName);

        return Task.CompletedTask;
    }
}
