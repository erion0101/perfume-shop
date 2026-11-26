namespace PerfumeShop.Modules.Notifications.Domain;

/// <param name="Encoding">"GSM-7" (160 characters per SMS) or "UCS-2" (70; any character outside the GSM alphabet, e.g. ë and ç).</param>
/// <param name="Length">Characters as the network counts them (GSM extension characters such as € count twice).</param>
/// <param name="NonGsmCharacters">The characters that forced UCS-2.</param>
internal sealed record SmsInfo(string Encoding, int Length, int Segments, int PerSegment, string NonGsmCharacters)
{
    public bool IsMultiSegment => Segments > 1;

    public string? Warning => IsMultiSegment
        ? $"{Segments} SMS segments ({Length} characters, {Encoding}, {PerSegment} per segment): every message is billed {Segments} times."
          + (NonGsmCharacters.Length > 0 ? $" Characters that force Unicode: {NonGsmCharacters}." : string.Empty)
        : null;
}

/// <summary>SMS encoding and segment count (3GPP TS 23.038), and Albanian transliteration to keep messages in GSM-7.</summary>
internal static class SmsText
{
    public const string Gsm7 = "GSM-7";
    public const string Ucs2 = "UCS-2";

    // GSM 03.38 basic character set (without the escape character). Note: Ç is in it, ç and ë are not.
    private const string Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    // Extension table: each costs two characters.
    private const string Extended = "^{}\\[~]|€\f";

    /// <summary>ë → e, ç → c (and capitals): Albanian text then fits the 160-character GSM alphabet.</summary>
    public static string Transliterate(string text) =>
        text.Replace('ë', 'e').Replace('Ë', 'E').Replace('ç', 'c').Replace('Ç', 'C');

    public static SmsInfo Analyze(string text)
    {
        var nonGsm = new string([.. text.Where(c => !Basic.Contains(c, StringComparison.Ordinal) && !Extended.Contains(c, StringComparison.Ordinal)).Distinct()]);
        if (nonGsm.Length == 0)
        {
            var length = text.Sum(c => Extended.Contains(c, StringComparison.Ordinal) ? 2 : 1);
            return new SmsInfo(Gsm7, length, Segments(length, 160, 153), length <= 160 ? 160 : 153, string.Empty);
        }

        // UCS-2 counts UTF-16 code units (an emoji is two).
        return new SmsInfo(Ucs2, text.Length, Segments(text.Length, 70, 67), text.Length <= 70 ? 70 : 67, nonGsm);
    }

    private static int Segments(int length, int single, int multi) =>
        length == 0 ? 1 : length <= single ? 1 : (length + multi - 1) / multi;
}
