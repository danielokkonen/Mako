using System.Globalization;
using Scraper.Core.Config;
using Scraper.Core.Exceptions;

namespace Scraper.Core.Extraction;

/// <summary>
/// Coerces raw extracted values to the field types declared by a job, applies defaults for absent or
/// uncoercible non-key fields, and enforces natural-key integrity. Shared by all item sources:
/// sources hand in already-transformed raw values (list fields as <see cref="IReadOnlyList{T}"/> of
/// elements) keyed by declared field name; this produces the final typed document.
/// </summary>
public static class FieldCoercion
{
    /// <summary>The separator joining key-field values into an item's natural key.</summary>
    public const string KeySeparator = "\u001F";

    /// <summary>
    /// Builds the final typed document for one extracted item.
    /// </summary>
    /// <param name="rawFields">Transformed raw values keyed by declared field name; list fields hold an element list.</param>
    /// <param name="job">The validated job (field mappings and natural key).</param>
    /// <param name="position">1-based position of the item within its page, for error messages.</param>
    public static ScrapedItem BuildItem(IReadOnlyDictionary<string, object?> rawFields, SiteJob job, int position)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in job.Fields)
        {
            if (field.List)
            {
                // No matches means absent: the default/null path applies, same as scalar fields.
                values[field.Name] = rawFields.TryGetValue(field.Name, out var listRaw) && listRaw is IReadOnlyList<object?> elements
                    ? elements.Select(e => Coerce(e, field.Type)).ToList()
                    : null;
            }
            else
            {
                object? value = rawFields.TryGetValue(field.Name, out var raw) ? Coerce(raw, field.Type) : null;

                if (value is null && job.Key.Contains(field.Name))
                    throw new PageExtractionException($"item {position}: key field '{field.Name}' is missing or uncoercible as type '{field.Type}'");

                value ??= Coerce(field.Default, field.Type);
                values[field.Name] = value;
            }
        }

        string key = string.Join(KeySeparator, job.Key.Select(name => FormatKeyPart(values[name])));
        return new ScrapedItem(key, values);
    }

    /// <summary>Coerces one raw value to the declared type; null when absent or uncoercible.</summary>
    public static object? Coerce(object? raw, FieldType type)
    {
        if (raw is null)
            return null;

        switch (type)
        {
            case FieldType.Text:
                // Raw values are primitives (string/long/double/bool); non-strings render invariant.
                return raw is string s ? s : Convert.ToString(raw, CultureInfo.InvariantCulture);

            case FieldType.Integer:
                long integral = 0;
                if (!ToLong(raw, out integral) || integral < int.MinValue || integral > int.MaxValue)
                    return null;
                return (int)integral;

            case FieldType.Long:
                return ToLong(raw, out long asLong) ? (object)asLong : null;

            case FieldType.Real:
                return ToDouble(raw, out double real) ? (object)real : null;

            case FieldType.Date:
                return ToDate(raw, out DateTimeOffset date) ? (object)date : null;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown field type.");
        }
    }

    private static bool ToLong(object raw, out long value)
    {
        if (raw is string s)
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (raw is long l)
        {
            value = l;
            return true;
        }
        if (raw is int i)
        {
            value = i;
            return true;
        }
        if (raw is double d && IsIntegral(d))
        {
            value = (long)d;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool ToDouble(object raw, out double value)
    {
        if (raw is string s)
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (ToLong(raw, out long integral))
        {
            value = integral;
            return true;
        }
        if (raw is double d)
        {
            value = d;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool ToDate(object raw, out DateTimeOffset value)
    {
        value = default;

        // ISO-8601 strings (offset-less values are assumed UTC); any other integral number is Unix epoch milliseconds.
        if (raw is string iso && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
            return true;

        long ms;
        if (!ToLong(raw, out ms))
            return false;

        try
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsIntegral(double d) => Math.Floor(d) == d && !double.IsNaN(d) && !double.IsInfinity(d);

    private static string FormatKeyPart(object? value) => value switch
    {
        null => throw new PageExtractionException("key field coerced to null; this is a bug in key validation"),
        string s => s,
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };
}
