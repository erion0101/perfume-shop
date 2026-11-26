namespace PerfumeShop.Modules.Notifications.Contracts;

/// <summary>Sends an SMS. The real provider is chosen later; until then only development has a sender.</summary>
public interface ISmsSender
{
    /// <summary>Provider name stored in the notification log (e.g. "console", "infobip").</summary>
    string Name => GetType().Name;

    /// <param name="phoneE164">Recipient, e.g. "+38344123456".</param>
    Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default);
}

/// <param name="Response">The provider's raw answer, stored in the notification log.</param>
public sealed record SmsSendResult(bool Succeeded, string? ProviderMessageId = null, string? Error = null, string? Response = null)
{
    public static SmsSendResult Sent(string? providerMessageId = null, string? response = null) => new(true, providerMessageId, Response: response);

    public static SmsSendResult Failed(string error, string? response = null) => new(false, Error: error, Response: response);
}
