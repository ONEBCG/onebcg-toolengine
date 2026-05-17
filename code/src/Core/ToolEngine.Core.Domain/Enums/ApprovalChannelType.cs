namespace ToolEngine.Core.Domain.Enums;

public enum ApprovalChannelType
{
    // No push — approver polls the dashboard (simplest, default dev channel).
    Dashboard      = 0,
    // Time-limited signed URL sent to approver email; one click approves/denies.
    EmailMagicLink = 1,
    // 6-digit OTP sent to approver email; required for Critical risk.
    EmailOtp       = 2,
    // JSON payload POSTed to tenant-configured webhook (Slack, Teams, custom).
    Webhook        = 3,
}
