namespace Scraper.Core.Serializers;

public static class JsonSerializer
{
    public static string ToJson<T>(IReadOnlyList<T> items)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        };
        return System.Text.Json.JsonSerializer.Serialize(items, options);
    }
}
