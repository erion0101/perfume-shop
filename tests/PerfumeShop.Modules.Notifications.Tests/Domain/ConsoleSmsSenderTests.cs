using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Modules.Notifications.Infrastructure.Sms;

namespace PerfumeShop.Modules.Notifications.Tests.Domain;

/// <summary>The Development console sender can also write SMS to a file (e2e tests read OTP codes from it).</summary>
public sealed class ConsoleSmsSenderTests
{
    [Fact]
    public async Task Appends_each_sms_as_a_json_line_when_a_file_is_configured()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.jsonl");
        var sender = new ConsoleSmsSender(NullLogger<ConsoleSmsSender>.Instance, Options.Create(new NotificationOptions { DevelopmentSmsFile = path }));

        await sender.SendAsync("+38344123456", "Kodi juaj: 123456", TestContext.Current.CancellationToken);
        await sender.SendAsync("+38344000000", "Tjetër", TestContext.Current.CancellationToken);

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal(("+38344123456", "Kodi juaj: 123456"), (first.RootElement.GetProperty("phone").GetString(), first.RootElement.GetProperty("message").GetString()));
        File.Delete(path);
    }

    [Fact]
    public async Task Writes_no_file_by_default()
    {
        var sender = new ConsoleSmsSender(NullLogger<ConsoleSmsSender>.Instance, Options.Create(new NotificationOptions()));

        var result = await sender.SendAsync("+38344123456", "x", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }
}
