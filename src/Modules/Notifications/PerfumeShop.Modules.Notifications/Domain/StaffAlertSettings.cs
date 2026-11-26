using System.Net.Mail;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Domain;

/// <summary>Who gets staff alerts, and which alerts are on (one row).</summary>
internal sealed class StaffAlertSettings : IAuditable
{
    public const int SingletonId = 1;
    public const int MaxRecipients = 10;

    public int Id { get; private set; } = SingletonId;

    public List<string> Recipients { get; private set; } = [];

    public bool NewOrder { get; private set; } = true;

    public bool FailedReceipt { get; private set; } = true;

    public bool LowStock { get; private set; } = true;

    public bool UnpaidDeliveries { get; private set; } = true;

    public bool JobFailures { get; private set; } = true;

    public string? UpdatedBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsOn(StaffAlertKind kind) => kind switch
    {
        StaffAlertKind.NewOrder => NewOrder,
        StaffAlertKind.FailedReceipt => FailedReceipt,
        StaffAlertKind.LowStock => LowStock,
        StaffAlertKind.UnpaidDeliveries => UnpaidDeliveries,
        StaffAlertKind.JobFailure => JobFailures,
        _ => false,
    };

    public Result Update(IReadOnlyCollection<string> recipients, bool newOrder, bool failedReceipt, bool lowStock, bool unpaidDeliveries, bool jobFailures, string actorId)
    {
        var cleaned = recipients.Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().ToList();
        if (cleaned.Count > MaxRecipients || cleaned.Any(r => r.Length > 200 || !MailAddress.TryCreate(r, out var address) || address.Address != r))
        {
            return Error.Validation("Notifications.Settings.RecipientsInvalid", $"Up to {MaxRecipients} valid email addresses.");
        }

        Recipients = cleaned;
        NewOrder = newOrder;
        FailedReceipt = failedReceipt;
        LowStock = lowStock;
        UnpaidDeliveries = unpaidDeliveries;
        JobFailures = jobFailures;
        UpdatedBy = actorId;
        return Result.Success();
    }
}
