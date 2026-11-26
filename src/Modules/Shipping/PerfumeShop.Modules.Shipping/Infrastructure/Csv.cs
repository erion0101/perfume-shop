using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PerfumeShop.Modules.Shipping.Infrastructure;

/// <summary>
/// Small RFC 4180 CSV reader/writer for courier files: comma or semicolon (Excel in Europe), quoted fields,
/// a UTF-8 BOM, CRLF, decimal comma. Headers are matched case-insensitively against aliases.
/// </summary>
internal static class Csv
{
    public sealed record Row(int LineNumber, IReadOnlyDictionary<string, string> Values)
    {
        public string? Get(params string[] names)
        {
            foreach (var name in names)
            {
                if (Values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }

    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>SHA-256 of the content with line endings and surrounding whitespace normalized.</summary>
    public static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().TrimStart('﻿'))));

    public static IReadOnlyList<Row> Read(string content)
    {
        var lines = SplitRecords(content.TrimStart('﻿'));
        if (lines.Count == 0)
        {
            return [];
        }

        var delimiter = lines[0].Text.Count(c => c == ';') > lines[0].Text.Count(c => c == ',') ? ';' : ',';
        var headers = ParseFields(lines[0].Text, delimiter).Select(NormalizeHeader).ToArray();

        List<Row> rows = [];
        foreach (var (lineNumber, text) in lines.Skip(1))
        {
            var fields = ParseFields(text, delimiter);
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < fields.Count; i++)
            {
                values[headers[i]] = fields[i];
            }

            rows.Add(new Row(lineNumber, values));
        }

        return rows;
    }

    /// <summary>"40.50", "40,50", "€ 40.50", "1.234,50" → 40.50 (invariant result).</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = new string([.. text.Where(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-')]);
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma > lastDot)
        {
            cleaned = cleaned.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else
        {
            cleaned = cleaned.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>CSV with a UTF-8 BOM so Excel shows ë/ç correctly.</summary>
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(';', headers.Select(Escape)).Append("\r\n");
        foreach (var row in rows)
        {
            builder.AppendJoin(';', row.Select(Escape)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([';', ',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static string NormalizeHeader(string header) =>
        string.Join('_', header.Trim().ToLowerInvariant().Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries));

    private static List<(int Line, string Text)> SplitRecords(string content)
    {
        List<(int, string)> records = [];
        var current = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var start = 1;
        foreach (var ch in content)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }

            if (ch == '\n')
            {
                line++;
                if (!inQuotes)
                {
                    records.Add((start, current.ToString().TrimEnd('\r')));
                    current.Clear();
                    start = line;
                    continue;
                }
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            records.Add((start, current.ToString().TrimEnd('\r')));
        }

        return records.Where(r => !string.IsNullOrWhiteSpace(r.Item2)).ToList();
    }

    private static List<string> ParseFields(string record, char delimiter)
    {
        List<string> fields = [];
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < record.Length; i++)
        {
            var ch = record[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < record.Length && record[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }

        fields.Add(field.ToString().Trim());
        return fields;
    }
}
