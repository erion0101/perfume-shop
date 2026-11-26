using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Domain;

internal enum NotificationChannel
{
    Sms = 1,
    Email = 2,
}

internal enum NotificationAudience
{
    Customer = 1,
    Staff = 2,
}

/// <summary>Staff alert types; each can be switched off in <see cref="StaffAlertSettings"/> (JobFailure also covers dead letters).</summary>
internal enum StaffAlertKind
{
    NewOrder = 1,
    FailedReceipt = 2,
    LowStock = 3,
    UnpaidDeliveries = 4,
    JobFailure = 5,
}

internal static class TemplateKeys
{
    public const string OrderPlaced = "order-placed";
    public const string OrderConfirmed = "order-confirmed";
    public const string ShipmentHandedOver = "shipment-handed-over";
    public const string ShipmentDelivered = "shipment-delivered";
    public const string OrderCancelled = "order-cancelled";
    public const string OrderExpired = "order-expired";
    public const string StaffNewOrder = "staff-new-order";
    public const string StaffReceiptFailed = "staff-receipt-failed";
    public const string StaffLowStock = "staff-low-stock";
    public const string StaffUnpaidDeliveries = "staff-unpaid-deliveries";
    public const string StaffJobFailed = "staff-job-failed";
    public const string StaffDeadLetter = "staff-dead-letter";
}

/// <summary>
/// What a template is for, which placeholders it may use (with sample values for the preview) and its default text.
/// Defined in code; the editable text lives in <see cref="NotificationTemplate"/>.
/// </summary>
internal sealed record TemplateDefinition(
    string Key,
    NotificationChannel Channel,
    NotificationAudience Audience,
    StaffAlertKind? Alert,
    string Description,
    string? DefaultSubject,
    string DefaultBody,
    IReadOnlyDictionary<string, string> Placeholders)
{
    public IReadOnlyCollection<string> PlaceholderNames => [.. Placeholders.Keys];
}

internal static class TemplateCatalog
{
    private static readonly Dictionary<string, string> Shop = new()
    {
        ["shopName"] = "PerfumeShop",
        ["shopPhone"] = "+383 44 000 000",
    };

    public static readonly IReadOnlyList<TemplateDefinition> All =
    [
        Sms(TemplateKeys.OrderPlaced, "Order placed (customer)",
            "{{shopName}}: Porosia {{orderNumber}} u pranua. Totali {{total}} EUR, paguani në dorëzim. Do t'ju telefonojmë për konfirmim.",
            ("orderNumber", "PS-2026-000042"), ("total", "41.90")),
        Sms(TemplateKeys.OrderConfirmed, "Order confirmed by staff (customer)",
            "{{shopName}}: Porosia {{orderNumber}} u konfirmua. Do t'ju njoftojmë kur të niset. Pyetje: {{shopPhone}}",
            ("orderNumber", "PS-2026-000042")),
        Sms(TemplateKeys.ShipmentHandedOver, "Parcel picked up by the courier (customer)",
            "{{shopName}}: Porosia {{orderNumber}} u nis me {{courier}}, nr. gjurmimit {{trackingNumber}}. Paguani {{codAmount}} EUR në dorëzim.",
            ("orderNumber", "PS-2026-000042"), ("courier", "Posta e Kosovës"), ("trackingNumber", "PK2026000001XK"), ("codAmount", "41.90")),
        Sms(TemplateKeys.ShipmentDelivered, "Parcel delivered: thank you (customer)",
            "{{shopName}}: Faleminderit! Porosia {{orderNumber}} u dorëzua. Shijojeni parfumin. Pyetje: {{shopPhone}}",
            ("orderNumber", "PS-2026-000042")),
        Sms(TemplateKeys.OrderCancelled, "Order cancelled (customer)",
            "{{shopName}}: Porosia {{orderNumber}} u anulua. Për pyetje: {{shopPhone}}",
            ("orderNumber", "PS-2026-000042")),
        Sms(TemplateKeys.OrderExpired, "Order expired: not confirmed in time (customer)",
            "{{shopName}}: Porosia {{orderNumber}} u anulua sepse nuk arritëm t'ju kontaktojmë për konfirmim. Na telefononi: {{shopPhone}}",
            ("orderNumber", "PS-2026-000042")),

        Staff(TemplateKeys.StaffNewOrder, StaffAlertKind.NewOrder, "New order placed (staff)",
            "Porosi e re {{orderNumber}} – {{total}} EUR",
            "Porosi e re {{orderNumber}} ({{placedAt}})\n\nKlienti: {{customerName}}, {{customerPhone}}, {{city}}\nTotali: {{total}} EUR (pagesë në dorëzim)\n\n{{items}}\n\nTelefononi klientin për konfirmim.",
            ("orderNumber", "PS-2026-000042"), ("total", "41.90"), ("placedAt", "26.11.2026 10:15"), ("customerName", "Arbër Krasniqi"),
            ("customerPhone", "+38344123456"), ("city", "Lipjan"), ("items", "1 × Lattafa Khamrah 100 ml – 39.90 EUR")),
        Staff(TemplateKeys.StaffReceiptFailed, StaffAlertKind.FailedReceipt, "Fiscal receipt could not be issued (staff)",
            "Kuponi fiskal dështoi: {{orderNumber}}",
            "Kuponi {{receiptNumber}} ({{kind}}) për porosinë {{orderNumber}} nuk u lëshua (prova {{attempts}}).\n\nGabimi: {{error}}\n\nMos e dorëzoni pakon pa kupon. Provoni përsëri: POST /api/admin/payments/receipts/{{receiptId}}/retry",
            ("receiptNumber", "PS-R-2026-000001"), ("kind", "Sale"), ("orderNumber", "PS-2026-000042"), ("attempts", "1"),
            ("error", "Fiscal service did not answer."), ("receiptId", "0193a1b2-0000-7000-8000-000000000000")),
        Staff(TemplateKeys.StaffLowStock, StaffAlertKind.LowStock, "Daily low-stock list (staff)",
            "Stok i ulët: {{count}} produkte ({{date}})",
            "Këto produkte janë në ose nën kufirin e stokut ({{date}}):\n\n{{items}}",
            ("count", "2"), ("date", "26.11.2026"), ("items", "LAT-KHA-100 Lattafa Khamrah 100 ml: 1 (kufiri 2)\nAJM-DAHN-3 Ajmal Dahn Al Oudh 3 ml: 0 (kufiri 2)")),
        Staff(TemplateKeys.StaffUnpaidDeliveries, StaffAlertKind.UnpaidDeliveries, "Delivered parcels not paid by the courier in time (staff)",
            "Pako të dorëzuara pa pagesë: {{count}} ({{amount}} EUR)",
            "Korrierët nuk i kanë paguar këto pako brenda afatit ({{date}}):\n\n{{items}}\n\nGjithsej: {{amount}} EUR",
            ("count", "1"), ("amount", "41.90"), ("date", "26.11.2026"),
            ("items", "Posta e Kosovës: PS-2026-000042 PK2026000001XK 41.90 EUR, dorëzuar 15.11.2026 (11 ditë, afati 7)")),
        Staff(TemplateKeys.StaffJobFailed, StaffAlertKind.JobFailure, "Background job failed (staff)",
            "Detyra në sfond dështoi: {{job}}",
            "Detyra {{job}} ({{recurringJob}}, id {{jobId}}) dështoi më {{failedAt}}.\n\n{{error}}\n\nShikoni Hangfire dhe logjet (Seq).",
            ("job", "OutboxProcessor`1.RunAsync"), ("recurringJob", "orders-outbox"), ("jobId", "1234"), ("failedAt", "26.11.2026 10:15"),
            ("error", "NpgsqlException: Connection refused")),
        Staff(TemplateKeys.StaffDeadLetter, StaffAlertKind.JobFailure, "Event could not be delivered after all retries (staff)",
            "Ngjarja nuk u dorëzua: {{eventType}} ({{module}})",
            "Ngjarja {{eventType}} (id {{messageId}}) në {{module}}.outbox_messages dështoi {{attempts}} herë dhe nuk do të provohet më.\nNdodhi më {{occurredAt}}, prova e fundit më {{failedAt}}.\n\nGabimi:\n{{error}}\n\nNdreqni shkakun, pastaj vendosni attempts = 0 për këtë rresht që të dërgohet përsëri.",
            ("eventType", "OrderShippedIntegrationEvent"), ("module", "orders"), ("messageId", "0193a1b2-0000-7000-8000-000000000000"), ("attempts", "10"),
            ("occurredAt", "26.11.2026 10:15"), ("failedAt", "26.11.2026 11:02"), ("error", "PostgresException: 23505 duplicate key value")),
    ];

    private static readonly Dictionary<string, TemplateDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static TemplateDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    private static TemplateDefinition Sms(string key, string description, string body, params (string Name, string Sample)[] placeholders) =>
        new(key, NotificationChannel.Sms, NotificationAudience.Customer, null, description, null, body, WithShop(placeholders));

    private static TemplateDefinition Staff(string key, StaffAlertKind alert, string description, string subject, string body, params (string Name, string Sample)[] placeholders) =>
        new(key, NotificationChannel.Email, NotificationAudience.Staff, alert, description, subject, body, WithShop(placeholders));

    private static Dictionary<string, string> WithShop((string Name, string Sample)[] placeholders)
    {
        var all = new Dictionary<string, string>(Shop, StringComparer.Ordinal);
        foreach (var (name, sample) in placeholders)
        {
            all[name] = sample;
        }

        return all;
    }
}

/// <summary>Editable text of one template (Admin). Placeholders are checked against the <see cref="TemplateDefinition"/>.</summary>
internal sealed class NotificationTemplate : IAuditable
{
    public const int SmsMaxLength = 480;
    public const int EmailMaxLength = 4000;
    public const int SubjectMaxLength = 200;

    private NotificationTemplate() { }

    public string Key { get; private set; } = null!;

    public NotificationChannel Channel { get; private set; }

    public NotificationAudience Audience { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; } = null!;

    /// <summary>SMS only: ë→e, ç→c before sending, so the message stays GSM-7 (160 instead of 70 characters per SMS).</summary>
    public bool Transliterate { get; private set; }

    public bool IsEnabled { get; private set; }

    public string? UpdatedBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static NotificationTemplate FromDefinition(TemplateDefinition definition) => new()
    {
        Key = definition.Key,
        Channel = definition.Channel,
        Audience = definition.Audience,
        Subject = definition.DefaultSubject,
        Body = definition.DefaultBody,
        Transliterate = definition.Channel == NotificationChannel.Sms,
        IsEnabled = true,
    };

    public static Result Validate(TemplateDefinition definition, string? subject, string? body)
    {
        var max = definition.Channel == NotificationChannel.Sms ? SmsMaxLength : EmailMaxLength;
        if (string.IsNullOrWhiteSpace(body) || body.Length > max)
        {
            return Error.Validation("Notifications.Template.BodyInvalid", $"Text is required (max {max} characters).");
        }

        if (definition.Channel == NotificationChannel.Email && (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectMaxLength))
        {
            return Error.Validation("Notifications.Template.SubjectInvalid", $"Email subject is required (max {SubjectMaxLength} characters).");
        }

        if (definition.Channel == NotificationChannel.Sms && subject is not null)
        {
            return Error.Validation("Notifications.Template.SubjectInvalid", "SMS templates have no subject.");
        }

        var checkedBody = TemplateText.Validate(body, definition.PlaceholderNames);
        return checkedBody.IsFailure || subject is null ? checkedBody : TemplateText.Validate(subject, definition.PlaceholderNames);
    }

    public Result Update(TemplateDefinition definition, string? subject, string body, bool transliterate, bool isEnabled, string actorId)
    {
        subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        var valid = Validate(definition, subject, body);
        if (valid.IsFailure)
        {
            return valid;
        }

        Subject = subject;
        Body = body.Trim();
        Transliterate = Channel == NotificationChannel.Sms && transliterate;
        IsEnabled = isEnabled;
        UpdatedBy = actorId;
        return Result.Success();
    }
}
