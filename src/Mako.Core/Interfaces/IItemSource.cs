using Mako.Core.Config;
using Mako.Core.Extraction;

namespace Mako.Core.Interfaces;

/// <summary>
/// A named extraction capability that turns one page's HTML into item documents.
/// Registered by name so job configurations select it via <c>source.kind</c>.
/// </summary>
public interface IItemSource : INamedCapability
{
    /// <param name="html">The raw page HTML.</param>
    /// <param name="pageUrl">Absolute URL of the page (for absolute-URL resolution and error messages).</param>
    /// <param name="job">The validated job: source configuration, field mappings, and natural key.</param>
    Task<ScrapedPage> ExtractAsync(string html, Uri pageUrl, SiteJob job);
}
