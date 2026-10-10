using Scraper.Core.Config;
using Scraper.Core.Extraction;

namespace Scraper.Core.Interfaces;

/// <summary>
/// A named pagination capability that derives the next-page URL from an extracted page.
/// Registered by name so job configurations select it via <c>pagination.strategy</c>.
/// Returns null when the current page is the last one.
/// </summary>
public interface IPaginationStrategy : INamedCapability
{
    string? NextPageUrl(ScrapedPage page, Uri pageUrl, PaginationConfig config);
}
