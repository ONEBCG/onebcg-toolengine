namespace ToolEngine.Infrastructure.Approval;

using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Pluggable notification channel for approval requests.
/// Each implementation sends the approval payload to a specific destination
/// (email, webhook, etc.) and returns when the send is complete.
/// The decision itself arrives via a separate callback endpoint — not here.
/// </summary>
public interface IApprovalChannel
{
    ApprovalChannelType ChannelType { get; }
    Task SendAsync(PendingApproval approval, CancellationToken ct = default);
}
