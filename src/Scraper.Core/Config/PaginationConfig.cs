namespace Scraper.Core.Config;

/// <summary>
/// The job's pagination strategy. <c>next-link</c> uses <see cref="Selector"/> (CSS);
/// <c>page-param</c> uses <see cref="Param"/>, <see cref="Current"/> and <see cref="Last"/>
/// (anchor-relative JSON paths to the page numbers). A job without pagination is single-page.
/// </summary>
public sealed record PaginationConfig(string Strategy, string? Selector, string? Param, string? Current, string? Last);
