using Mako.Core.Extraction;

namespace Mako.Core.Serializers;

public static class JsonSerializer
{
    /// <summary>The snapshot as a JSON array of the item documents, exactly as stored.</summary>
    public static string ToJson(IReadOnlyList<ScrapedItem> items)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        };

        return System.Text.Json.JsonSerializer.Serialize(items.Select(i => i.Fields).ToList(), options);
    }
}
