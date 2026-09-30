namespace Scraper.Core.Interfaces;

public sealed record ScrapePageResult<T>(IReadOnlyList<T> Items, string? NextPageUrl);

public interface IScraper<T>
{
    Task<ScrapePageResult<T>> ScrapeAsync(string html, string baseUrl);
}
