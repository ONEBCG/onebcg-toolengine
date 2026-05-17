namespace ToolEngine.Infrastructure.Approval;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Sends a time-limited signed URL to the approver's email.
/// One click on the link calls POST /approvals/{token}/decide.
///
/// Production: swap IEmailSender stub with SendGrid / SES / SMTP implementation.
/// </summary>
public sealed class EmailMagicLinkChannel : IApprovalChannel
{
    private readonly IEmailSender     _email;
    private readonly ApprovalOptions  _options;
    private readonly ILogger<EmailMagicLinkChannel> _log;

    public ApprovalChannelType ChannelType => ApprovalChannelType.EmailMagicLink;

    public EmailMagicLinkChannel(
        IEmailSender                  email,
        IOptions<ApprovalOptions>     options,
        ILogger<EmailMagicLinkChannel> log)
    {
        _email   = email;
        _options = options.Value;
        _log     = log;
    }

    public async Task SendAsync(PendingApproval approval, CancellationToken ct = default)
    {
        var to = approval.ApproverEmail
                 ?? throw new InvalidOperationException(
                     $"PendingApproval {approval.Id} has no ApproverEmail set for EmailMagicLink channel.");

        var approveUrl = $"{_options.BaseUrl}/approvals/{approval.ApprovalToken}/decide?action=approve";
        var denyUrl    = $"{_options.BaseUrl}/approvals/{approval.ApprovalToken}/decide?action=deny";

        var subject = $"[ToolEngine] Approval required: {approval.ToolFullName}";
        var body = $"""
            A tool invocation requires your approval.

            Tool:    {approval.ToolFullName}
            Risk:    {approval.Risk}
            Reason:  {approval.ApprovalReason}
            Tenant:  {approval.TenantId}
            Expires: {approval.ExpiresAt:u}

            APPROVE: {approveUrl}
            DENY:    {denyUrl}

            This link expires at {approval.ExpiresAt:u}.
            """;

        _log.LogInformation(
            "Sending magic-link approval email to {To} for {ToolFullName} (invocationId={Id})",
            to, approval.ToolFullName, approval.Id);

        await _email.SendAsync(to, subject, body, ct);
    }
}
