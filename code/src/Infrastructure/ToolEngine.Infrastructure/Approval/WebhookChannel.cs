namespace ToolEngine.Infrastructure.Approval;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// POSTs the approval payload to a tenant-configured webhook URL (Slack, Teams, custom).
/// The webhook endpoint should POST back to /approvals/{token}/decide with
/// { "action": "approve"|"deny", "decidedByUserId": "..." }.
/// </summary>
public sealed class WebhookChannel : IApprovalChannel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ApprovalOptions    _options;
    private readonly ILogger<WebhookChannel> _log;

    public ApprovalChannelType ChannelType => ApprovalChannelType.Webhook;

    public WebhookChannel(
        IHttpClientFactory         httpFactory,
        IOptions<ApprovalOptions>  options,
        ILogger<WebhookChannel>    log)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _log         = log;
    }

    public async Task SendAsync(PendingApproval approval, CancellationToken ct = default)
    {
        var webhookUrl = _options.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _log.LogWarning(
                "WebhookChannel: no WebhookUrl configured. Approval {Id} not sent.",
                approval.Id);
            return;
        }

        var payload = new
        {
            invocationId  = approval.Id,
            toolFullName  = approval.ToolFullName,
            risk          = approval.Risk.ToString(),
            reason        = approval.ApprovalReason,
            tenantId      = approval.TenantId,
            requestedBy   = approval.UserId,
            expiresAt     = approval.ExpiresAt,
            approveUrl    = $"{_options.BaseUrl}/approvals/{approval.ApprovalToken}/decide?action=approve",
            denyUrl       = $"{_options.BaseUrl}/approvals/{approval.ApprovalToken}/decide?action=deny",
        };

        _log.LogInformation(
            "WebhookChannel: sending approval request for {ToolFullName} to {WebhookUrl}",
            approval.ToolFullName, webhookUrl);

        var client = _httpFactory.CreateClient("approval-webhook");
        var resp   = await client.PostAsJsonAsync(webhookUrl, payload, ct);

        if (!resp.IsSuccessStatusCode)
            _log.LogWarning(
                "WebhookChannel: webhook returned {StatusCode} for invocation {Id}",
                (int)resp.StatusCode, approval.Id);
    }
}
