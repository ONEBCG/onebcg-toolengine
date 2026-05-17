namespace ToolEngine.Infrastructure.Approval;

using Microsoft.Extensions.Logging;

/// <summary>
/// Dev-stub IEmailSender. Logs email content to the console instead of sending.
/// Replace with SendGrid, SES, or SMTP implementation for production.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;

    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _log.LogInformation(
            "LoggingEmailSender: [DEV] email to={To} subject={Subject}\n{Body}",
            to, subject, body);
        return Task.CompletedTask;
    }
}
