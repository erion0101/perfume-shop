using System.Globalization;
using System.Text;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Common;

/// <summary>
/// Lowercase, URL-safe identifier: "Maison Alhambra" → "maison-alhambra".
/// Albanian letters are transliterated (ë → e, ç → c) as are other diacritics.
/// Uniqueness is enforced by the database; on a clash the caller retries with <see cref="WithSuffix"/>.
/// </summary>
internal sealed record Slug
{
    public const int MaxLength = 200;

    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Result<Slug> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<Slug>.Failure(SlugErrors.Empty);
        }

        var builder = new StringBuilder(input.Length);
        var pendingSeparator = false;

        foreach (var ch in Transliterate(input))
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingSeparator = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is not ('\'' or '’' or '`'))
            {
                // Apostrophes are dropped ("L'Homme" → "lhomme"); everything else separates words.
                pendingSeparator = true;
            }
        }

        if (builder.Length == 0)
        {
            return Result<Slug>.Failure(SlugErrors.Empty);
        }

        return builder.Length > MaxLength
            ? Result<Slug>.Failure(SlugErrors.TooLong)
            : Result<Slug>.Success(new Slug(builder.ToString()));
    }

    /// <summary>"lattafa-khamrah" → "lattafa-khamrah-2" when the first one is taken.</summary>
    public Result<Slug> WithSuffix(int number) =>
        number < 2
            ? Result<Slug>.Failure(SlugErrors.InvalidSuffix)
            : Create($"{Value}-{number}");

    public override string ToString() => Value;

    private static string Transliterate(string input)
    {
        var builder = new StringBuilder(input.Length);

        // FormD splits "ë" into "e" + combining diaeresis and "ç" into "c" + combining cedilla.
        foreach (var ch in input.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(ch switch
            {
                'ß' => "ss",
                'æ' or 'Æ' => "ae",
                'œ' or 'Œ' => "oe",
                'ø' or 'Ø' => "o",
                'đ' or 'Đ' => "d",
                'ł' or 'Ł' => "l",
                '&' => " and ",
                _ => ch.ToString(),
            });
        }

        return builder.ToString();
    }
}
