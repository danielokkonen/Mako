using System.Reflection;

namespace Scraper.Core.Serializers;

public static class MarkdownSerializer
{
    public static string ToMarkdown<T>(IReadOnlyList<T> items, string sourceUrl)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var sb = new StringBuilder();
        sb.AppendLine($"# Scraped results — {sourceUrl}");
        sb.AppendLine();
        sb.AppendLine($"{items.Count} item(s)");

        for (int i = 0; i < items.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"## Item {i + 1}");
            foreach (var prop in props)
                sb.AppendLine($"- **{ToCamelCase(prop.Name)}**: {RenderValue(prop.GetValue(items[i]))}");
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
