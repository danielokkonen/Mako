using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Mako.Core.Config;
using Mako.Core.Interfaces;

namespace Mako.Core.Extraction;

/// <summary>
/// The <c>dom</c> item source: items are the elements matched by the configured CSS selector. Scalar
/// fields read a sub-element's trimmed text within each item element; list fields collect every match
/// in document order (zero matches means absent, so the default/null path applies). When a field's
/// selector matches nothing inside an item it falls back to the same selector at page level, so one
/// mapping can carry both row-level and page-level values. A field with <see cref="FieldMapping.Attribute"/>
/// reads that attribute of the matched element instead of its text content; an absent element or
/// attribute still flows to the default/null path. A page whose items selector matches nothing fails
/// the page, naming the selector.
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
            var rawFields = BuildRawFields(itemElements[position], document, job);
            items.Add(FieldCoercion.BuildItem(rawFields, job, position + 1));
        }

        return Task.FromResult(new ScrapedPage(items, document, null));
    }

    private Dictionary<string, object?> BuildRawFields(IElement item, IDocument document, SiteJob job)
    {
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in job.Fields)
        {
            if (field.Source is null)
                continue; // Constant fields take their value from the declared default.

            if (field.List)
            {
                // Zero matches inside the item falls back to page level; zero there means absent, so the
                // default/null path applies, same as scalar fields.
                var elements = item.QuerySelectorAll(field.Source);
                if (elements.Count == 0)
                    elements = document.QuerySelectorAll(field.Source);
                if (elements.Count > 0)
                    raw[field.Name] = elements.Select(e => ReadValue(e, field)).ToList();
            }
            else
            {
                IElement? element = item.QuerySelector(field.Source) ?? document.QuerySelector(field.Source);
                object? value = element is null ? null : ReadValue(element, field);
                raw[field.Name] = value;
            }
        }

        return raw;
    }

    private object? ReadValue(IElement element, FieldMapping field)
    {
        string? text = field.Attribute is null ? element.TextContent.Trim() : element.GetAttribute(field.Attribute)?.Trim();
        return TransformApplier.Apply(_transforms, field.Transform, text);
    }
}
