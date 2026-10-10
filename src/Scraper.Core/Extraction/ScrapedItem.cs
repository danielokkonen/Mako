namespace Scraper.Core.Extraction;

/// <summary>
/// One scraped item as an ordered document: a natural key plus its field values in the order
/// declared by the job configuration. Values are already coerced to their final types
/// (string, int, long, double, bool, DateTimeOffset).
/// </summary>
public sealed record ScrapedItem(string Key, IReadOnlyDictionary<string, object?> Fields)
{
    /// <summary>The document as a compact JSON object; this is what gets stored in the jsonb column.</summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(Fields);
}
