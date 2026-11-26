using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Notifications.Contracts;

namespace PerfumeShop.Modules.Notifications.Infrastructure.Sms;

/// <summary>
/// DEVELOPMENT ONLY: writes the SMS (including OTP codes) to the log instead of sending it, and — when
/// Notifications:DevelopmentSmsFile is set (end-to-end tests) — appends it as one JSON line to that file.
/// Registered only when the environment is Development; see <see cref="NotificationsModule"/>.
/// </summary>
internal sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger, IOptions<NotificationOptions> options) : ISmsSender
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public string Name => "console";

    public async Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("DEV SMS to {Phone}: {Message}", phoneE164, message);
        if (options.Value.DevelopmentSmsFile is { Length: > 0 } path)
        {
            var line = JsonSerializer.Serialize(new { phone = phoneE164, message, atUtc = DateTime.UtcNow }) + "\n";
            await FileLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(path, line, cancellationToken);
            }
            finally
            {
                FileLock.Release();
            }
        }

        return SmsSendResult.Sent($"console-{Guid.NewGuid():N}");
    }
}
