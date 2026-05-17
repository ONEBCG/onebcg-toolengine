namespace ToolEngine.Infrastructure.Approval;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Configuration for the approval engine.
/// Bind from appsettings.json section "Approval".
/// </summary>
public sealed class ApprovalOptions
{
    // Default channel when no tenant override is set.
    public ApprovalChannelType DefaultChannel { get; set; } = ApprovalChannelType.Dashboard;

    // Minutes before a Pending approval expires. 0 = no expiry.
    public int ApprovalTimeoutMinutes { get; set; } = 60;

    // Base URL used to build magic-link and OTP verify URLs in emails.
    // E.g. "https://app.onebcg.com" — path appended by the channel.
    public string BaseUrl { get; set; } = "http://localhost:5174";

    // Webhook URL for the Webhook channel (overridden per-tenant in production).
    public string? WebhookUrl { get; set; }

    // Override channel per tenant: key = tenantId, value = channel type.
    public Dictionary<string, ApprovalChannelType> TenantChannelOverrides { get; set; } = new();
}
