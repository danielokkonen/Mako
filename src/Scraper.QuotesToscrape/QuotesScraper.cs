using AngleSharp.Html.Parser;
using Scraper.Core.Interfaces;

namespace Scraper.QuotesToscrape;

public sealed record Quote(string Text, string Author, IReadOnlyList<string> Tags);

public sealed class QuotesScraper : IScraper<Quote>
{
    private static readonly HtmlParser Parser = new();

    public Task<ScrapePageResult<Quote>> ScrapeAsync(string html, string baseUrl)
    {
        var doc = Parser.ParseDocument(html);

        var quotes = doc.QuerySelectorAll("div.quote")
            .Select(q => new Quote(
                q.QuerySelector(".text")!.TextContent.Trim().Trim('“', '”'),
                q.QuerySelector(".author")!.TextContent,
                q.QuerySelectorAll("a.tag").Select(t => t.TextContent).ToList()))
            .ToList();

        string? next = null;
        var nextLink = doc.QuerySelector("li.next a");
        if (nextLink is not null)
        {
            string? href = nextLink.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
                next = new Uri(new Uri(baseUrl), href).ToString();
        }

        return Task.FromResult(new ScrapePageResult<Quote>(quotes, next));
    }
}
