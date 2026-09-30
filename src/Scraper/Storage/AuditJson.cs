namespace Scraper.Core.Storage;

/// <summary>Serializes entity before/after images for audit rows (compact, camelCase).</summary>
public static class AuditJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
}
