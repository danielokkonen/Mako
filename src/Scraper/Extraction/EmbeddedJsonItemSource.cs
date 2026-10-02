using System.Text.Json;
using AngleSharp.Html.Parser;
using JsonCons.JsonPath;
using Scraper.Core.Config;
using Scraper.Core.Interfaces;

namespace Scraper.Core.Extraction;

/// <summary>
/// The <c>embedded-json</c> item source: the page embeds its data as a base64 JSON payload inside a
/// marked script tag. The anchor path must select exactly one node (the page-data object), the items
/// path exactly one array within it, and each field path is evaluated relative to an item. A missing
/// or malformed payload fails the page with an identifying error.
/// </summary>
public sealed class EmbeddedJsonItemSource(IEnumerable<IFieldTransform> transforms) : IItemSource
{
    private static readonly HtmlParser Parser = new();

    private readonly IReadOnlyDictionary<string, IFieldTransform> _transforms = TransformApplier.Index(transforms);

    public string Name => "embedded-json";

    public Task<ScrapedPage> ExtractAsync(string html, Uri pageUrl, SiteJob job)
    {
        var document = Parser.ParseDocument(html);
        var source = job.Source;

        byte[] payload = ReadPayloadBytes(html, source.Marker!);
        // The JsonDocument is deliberately kept alive (not disposed): readPayloadValue below resolves
        // pagination paths against it for the rest of this page's lifetime. One per page, bounded by MaxPages.
        var payloadDoc = ParseJson(payload);

        JsonElement anchorNode = SelectSingle(JsonPaths.Parse(source.Anchor!, "anchor"), source.Anchor!, payloadDoc.RootElement);

        var itemArrays = JsonPaths.Parse(source.Items, "items path").Select(anchorNode, null)
            .Where(c => c.ValueKind == JsonValueKind.Array)
            .ToList();

        if (itemArrays.Count != 1)
            throw new PageExtractionException($"Items path '{source.Items}' selected {itemArrays.Count} array(s) relative to the anchor; exactly one is required");

        var itemArray = itemArrays[0];

        // Parsed once per page, not per item.
        var fieldSelectors = job.Fields.Select(f => (Field: f, Selector: JsonPaths.Parse(f.Source, $"field '{f.Name}' source path"))).ToList();

        var items = new List<ScrapedItem>(itemArray.GetArrayLength());
        int position = 0;
        foreach (var itemElement in itemArray.EnumerateArray())
        {
            position++;
            var rawFields = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var entry in fieldSelectors)
            {
                var field = entry.Field;
                var selector = entry.Selector;

                IList<JsonElement> values = selector.Select(itemElement, null);

                if (field.List)
                {
                    // A list field's path must select an array; anything else is absent.
                    rawFields[field.Name] = values.Count > 0 && values[0].ValueKind == JsonValueKind.Array
                        ? values[0].EnumerateArray().Select(e => TransformApplier.Apply(_transforms, field.Transform, ToPrimitive(e))).ToList()
                        : null;
                }
                else
                {
                    object? value = values.Count > 0 ? ToPrimitive(values[0]) : null;
                    rawFields[field.Name] = TransformApplier.Apply(_transforms, field.Transform, value);
                }
            }

            items.Add(FieldCoercion.BuildItem(rawFields, job, position));
        }

        Func<string, object?> readPayloadValue = path =>
        {
            var values = JsonPaths.Parse(path, "payload path").Select(anchorNode, null);
            return values.Count > 0 ? ToPrimitive(values[0]) : null;
        };

        return Task.FromResult(new ScrapedPage(items, document, readPayloadValue));
    }

    private static byte[] ReadPayloadBytes(string html, string marker)
    {
        int start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new PageExtractionException($"Marker '{marker}' not found in page");

        start += marker.Length;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0)
            throw new PageExtractionException($"No closing </script> after marker '{marker}'");

        string content = html[start..end].Trim();

        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException ex)
        {
            throw new PageExtractionException("Marker payload is not valid base64", ex);
        }
    }

    private static JsonDocument ParseJson(byte[] payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new PageExtractionException("Marker payload is not valid JSON", ex);
        }
    }

    private static JsonElement SelectSingle(JsonSelector selector, string path, JsonElement root)
    {
        var matches = selector.Select(root, null);
        if (matches.Count == 0)
            throw new PageExtractionException($"Anchor '{path}' matched no nodes in the payload");
        if (matches.Count > 1)
            throw new PageExtractionException($"Anchor '{path}' matched {matches.Count} nodes; exactly one is required");

        return matches[0];
    }

    private static object? ToPrimitive(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out long integral) ? (object)integral : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null, // Null / Object / Array: no primitive representation; treated as absent.
    };
}
