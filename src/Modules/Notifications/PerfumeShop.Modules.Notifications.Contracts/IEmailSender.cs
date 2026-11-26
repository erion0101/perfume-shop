namespace PerfumeShop.Modules.Notifications.Contracts;

/// <summary>Sends an email (Brevo later). Outside Development there is no sender yet: every send fails.</summary>
public interface IEmailSender
{
    string Name => GetType().Name;

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <param name="TextBody">Plain text (staff alerts); HTML templates come with the customer emails later.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody);

public sealed record EmailSendResult(bool Succeeded, string? ProviderMessageId = null, string? Error = null, string? Response = null)
{
    public static EmailSendResult Sent(string? providerMessageId = null, string? response = null) => new(true, providerMessageId, Response: response);

    public static EmailSendResult Failed(string error, string? response = null) => new(false, Error: error, Response: response);
}
