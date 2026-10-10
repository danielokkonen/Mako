using AngleSharp.Dom;
using Scraper.Core.Config;
using Scraper.Core.Extraction;
using Scraper.Core.Interfaces;

namespace Scraper.Core.Pagination;

/// <summary>
/// The <c>next-link</c> pagination strategy: the next page is wherever the configured link points,
/// resolved absolute against the page URL. No match or an unusable href means the current page is
/// the last one.
/// </summary>
public sealed class NextLinkPagination : IPaginationStrategy
{
    public string Name => "next-link";

    public string? NextPageUrl(ScrapedPage page, Uri pageUrl, PaginationConfig config)
    {
        IElement? link = page.Document.QuerySelector(config.Selector!);
        if (link is null)
            return null;

        string? href = link.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
            return null;

        try
        {
            return new Uri(pageUrl, href).ToString();
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            // Unusable href: treat as last page.
            return null;
        }
    }
}
