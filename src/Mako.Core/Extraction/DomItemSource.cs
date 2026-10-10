using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Mako.Core.Config;
using Mako.Core.Interfaces;

namespace Mako.Core.Extraction;

/// <summary>
/// The <c>dom</c> item source: items are the elements matched by the configured CSS selector. Scalar
/// fields read a sub-element's trimmed text within each item element; list fields collect every match
/// in document order (zero matches means absent, so the default/null path applies). A page whose
/// items selector matches nothing fails the page, naming the selector.
/// </summary>
public sealed class DomItemSource(IEnumerable<IFieldTransform> transforms) : IItemSource
{
    private static readonly HtmlParser Parser = new();

    private readonly IReadOnlyDictionary<string, IFieldTransform> _transforms = TransformApplier.Index(transforms);

    public string Name => "dom";

    public Task<ScrapedPage> ExtractAsync(string html, Uri pageUrl, SiteJob job)
    {
        var document = Parser.ParseDocument(html);

        var itemElements = document.QuerySelectorAll(job.Source.Items);
        if (itemElements.Count == 0)
            throw new PageExtractionException($"No elements matched items selector '{job.Source.Items}'");

        var items = new List<ScrapedItem>(itemElements.Count);
        for (int position = 0; position < itemElements.Count; position++)
        {
            var rawFields = BuildRawFields(itemElements[position], job);
            items.Add(FieldCoercion.BuildItem(rawFields, job, position + 1));
        }

        return Task.FromResult(new ScrapedPage(items, document, null));
    }

    private Dictionary<string, object?> BuildRawFields(IElement item, SiteJob job)
    {
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in job.Fields)
        {
            if (field.List)
            {
                // Zero matches means absent: the default/null path applies, same as scalar fields.
                var elements = item.QuerySelectorAll(field.Source);
                if (elements.Count > 0)
                    raw[field.Name] = elements.Select(e => TransformApplier.Apply(_transforms, field.Transform, e.TextContent.Trim())).ToList();
            }
            else
            {
                IElement? element = item.QuerySelector(field.Source);
                object? value = element is null ? null : TransformApplier.Apply(_transforms, field.Transform, element.TextContent.Trim());
                raw[field.Name] = value;
            }
        }

        return raw;
    }
}
