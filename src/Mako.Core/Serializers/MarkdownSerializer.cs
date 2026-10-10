using Mako.Core.Extraction;

namespace Mako.Core.Serializers;

public static class MarkdownSerializer
{
    /// <summary>
    /// Renders the item documents: one section per item, one bullet per field in declared order.
    /// </summary>
    public static string ToMarkdown(IReadOnlyList<ScrapedItem> items, string sourceUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Scraped results — {sourceUrl}");
        sb.AppendLine();
        sb.AppendLine($"{items.Count} item(s)");

        for (int i = 0; i < items.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"## Item {i + 1}");
            foreach (var field in items[i].Fields)
                sb.AppendLine($"- **{ToCamelCase(field.Key)}**: {RenderValue(field.Value)}");
        }

        return sb.ToString();
    }

    private static string RenderValue(object? value) => value switch
    {
            null => "—",
            string s => s,
            System.Collections.IEnumerable e when e is not string =>
                string.Join(", ", e.Cast<object?>().Select(RenderValue)),
            _ => value.ToString() ?? "—",
        };

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
