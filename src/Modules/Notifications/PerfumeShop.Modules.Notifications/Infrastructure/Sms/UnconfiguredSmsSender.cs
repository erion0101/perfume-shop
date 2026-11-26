using Microsoft.Extensions.Logging;
using PerfumeShop.Modules.Notifications.Contracts;

namespace PerfumeShop.Modules.Notifications.Infrastructure.Sms;

/// <summary>Used outside Development until a real SMS provider is added: every send fails loudly.</summary>
internal sealed class UnconfiguredSmsSender(ILogger<UnconfiguredSmsSender> logger) : ISmsSender
{
    public string Name => "unconfigured";

    public Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        logger.LogError("No SMS provider is configured; SMS to {Phone} was not sent.", phoneE164);
        return Task.FromResult(SmsSendResult.Failed("No SMS provider configured."));
    }
}
