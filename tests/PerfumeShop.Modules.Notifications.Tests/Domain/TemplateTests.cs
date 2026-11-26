using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Notifications.Infrastructure;

namespace PerfumeShop.Modules.Notifications.Tests.Domain;

public sealed class TemplateTests
{
    private static readonly NotificationOptions Options = new();

    [Fact]
    public void Placeholders_are_replaced_and_missing_values_become_empty()
    {
        var text = TemplateText.Render("Porosia {{orderNumber}} – {{ total }} EUR {{nothing}}.", new Dictionary<string, string> { ["orderNumber"] = "PS-1", ["total"] = "9.90" });

        Assert.Equal("Porosia PS-1 – 9.90 EUR .", text);
    }

    [Fact]
    public void Unknown_placeholders_are_rejected_with_the_allowed_list()
    {
        var result = TemplateText.Validate("Hi {{orderNumber}} {{price}} {{Total}}", ["orderNumber", "total"]);

        Assert.Equal("Notifications.Template.UnknownPlaceholder", result.Error.Code);
        Assert.Contains("{{price}}, {{Total}}", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Allowed: {{orderNumber}}, {{total}}", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Porosia {{orderNumber}")]
    [InlineData("Porosia {{ order-number }}")]
    [InlineData("Porosia }}")]
    public void Malformed_placeholders_are_rejected(string text) =>
        Assert.Equal("Notifications.Template.MalformedPlaceholder", TemplateText.Validate(text, ["orderNumber"]).Error.Code);

    [Fact]
    public void Every_default_template_uses_only_its_own_placeholders()
    {
        foreach (var d in TemplateCatalog.All)
        {
            Assert.True(NotificationTemplate.Validate(d, d.DefaultSubject, d.DefaultBody).IsSuccess, d.Key);
        }
    }

    [Fact]
    public void Every_default_customer_sms_fits_one_gsm_segment_with_sample_data()
    {
        foreach (var d in TemplateCatalog.All.Where(d => d.Channel == NotificationChannel.Sms))
        {
            var sms = NotificationQueue.Render(NotificationTemplate.FromDefinition(d), d, d.Placeholders, Options).Sms!;

            Assert.True(sms is { Encoding: SmsText.Gsm7, Segments: 1 }, $"{d.Key}: {sms}");
        }
    }

    [Fact]
    public void Without_transliteration_albanian_letters_double_the_cost()
    {
        var d = TemplateCatalog.Find(TemplateKeys.OrderPlaced)!;

        var raw = NotificationQueue.Render(d, null, d.DefaultBody, transliterate: false, d.Placeholders, Options);
        var cheap = NotificationQueue.Render(d, null, d.DefaultBody, transliterate: true, d.Placeholders, Options);

        Assert.Equal((SmsText.Ucs2, 2), (raw.Sms!.Encoding, raw.Sms.Segments));
        Assert.Contains("ë", raw.Sms.NonGsmCharacters, StringComparison.Ordinal);
        Assert.NotNull(raw.Sms.Warning);
        Assert.Contains("paguani ne dorezim", cheap.Body, StringComparison.Ordinal);
        Assert.Null(cheap.Sms!.Warning);
    }

    [Fact]
    public void Template_update_validates_text_subject_and_placeholders()
    {
        var sms = TemplateCatalog.Find(TemplateKeys.OrderPlaced)!;
        var email = TemplateCatalog.Find(TemplateKeys.StaffNewOrder)!;
        var template = NotificationTemplate.FromDefinition(sms);

        Assert.Equal("Notifications.Template.UnknownPlaceholder", template.Update(sms, null, "{{orderNumber}} {{iban}}", true, true, "a").Error.Code);
        Assert.Equal("Notifications.Template.SubjectInvalid", template.Update(sms, "Subject", "{{orderNumber}}", true, true, "a").Error.Code);
        Assert.Equal("Notifications.Template.BodyInvalid", template.Update(sms, null, new string('x', 481), true, true, "a").Error.Code);
        Assert.Equal("Notifications.Template.SubjectInvalid", NotificationTemplate.FromDefinition(email).Update(email, null, "x", false, true, "a").Error.Code);
        Assert.Equal("Notifications.Template.UnknownPlaceholder",
            NotificationTemplate.FromDefinition(email).Update(email, "Porosi {{iban}}", "x", false, true, "a").Error.Code);

        Assert.True(template.Update(sms, null, "Porosia {{orderNumber}}", false, false, "admin").IsSuccess);
        Assert.Equal(("Porosia {{orderNumber}}", false, false, "admin"), (template.Body, template.Transliterate, template.IsEnabled, template.UpdatedBy));
    }

    [Fact]
    public void Staff_alert_recipients_must_be_valid_emails()
    {
        var settings = new StaffAlertSettings();

        Assert.True(settings.Update(["not-an-email"], true, true, true, true, true, "a").IsFailure);
        Assert.True(settings.Update([.. Enumerable.Range(0, 11).Select(i => $"s{i}@example.com")], true, true, true, true, true, "a").IsFailure);
        Assert.True(settings.Update([" Staff@Example.com ", "staff@example.com"], true, false, true, true, true, "a").IsSuccess);
        Assert.Equal(["staff@example.com"], settings.Recipients);
        Assert.False(settings.IsOn(StaffAlertKind.FailedReceipt));
    }
}
