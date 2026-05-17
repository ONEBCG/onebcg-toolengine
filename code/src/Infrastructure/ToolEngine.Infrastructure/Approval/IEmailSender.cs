namespace ToolEngine.Infrastructure.Approval;

/// <summary>
/// Minimal email abstraction used by approval channels.
/// Dev stub: LoggingEmailSender (logs content, sends nothing).
/// Production: swap with SendGrid, SES, or SMTP implementation.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
