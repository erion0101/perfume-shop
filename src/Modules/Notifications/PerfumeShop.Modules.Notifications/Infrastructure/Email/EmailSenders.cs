using Microsoft.Extensions.Logging;
using PerfumeShop.Modules.Notifications.Contracts;

namespace PerfumeShop.Modules.Notifications.Infrastructure.Email;

/// <summary>DEVELOPMENT ONLY: writes the email to the log instead of sending it. Registered only in Development.</summary>
internal sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public string Name => "console";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("DEV EMAIL to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.FromResult(EmailSendResult.Sent($"console-{Guid.NewGuid():N}", """{"dev":"written to the log, not sent"}"""));
    }
}

/// <summary>Used outside Development until Brevo is added: every send fails loudly (and is retried, then marked Failed).</summary>
internal sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public string Name => "unconfigured";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogError("No email provider is configured; email to {To} ({Subject}) was not sent.", message.To, message.Subject);
        return Task.FromResult(EmailSendResult.Failed("No email provider configured."));
    }
}
