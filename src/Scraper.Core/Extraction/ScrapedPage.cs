using AngleSharp.Html.Dom;

namespace Scraper.Core.Extraction;

/// <summary>
/// The result of extracting one page: the item documents, the parsed HTML document (always
/// available so pagination strategies can inspect the DOM), and — for <c>embedded-json</c>
/// sources only — a reader that resolves an anchor-relative JSON path to a value.
/// </summary>
public sealed record ScrapedPage(
    IReadOnlyList<ScrapedItem> Items,
    IHtmlDocument Document,
    Func<string, object?>? ReadPayloadValue);
