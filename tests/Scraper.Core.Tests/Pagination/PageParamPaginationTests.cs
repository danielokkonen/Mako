namespace Scraper.Core.Tests.Pagination;

public class PageParamPaginationTests
{
    private static readonly Uri PageUrl = new("https://example.com/list?sort=name&page=1&x=1");

    [Test]
    public void MiddlePage_NextUrlSetsParamToCurrentPlusOne_PreservingOtherParamsInOrder()
    {
        var page = Page(path => path switch
        {
            ".current" => 2L,
            ".last" => 5L,
            _ => null,
        });

        string? next = new PageParamPagination().NextPageUrl(page, PageUrl, Config());

        Assert.That(next, Is.EqualTo("https://example.com/list?sort=name&page=3&x=1"));
    }

    [Test]
    public void ExistingParam_ReplacedInPlaceWithoutDuplicate()
    {
        var page = Page(path => path switch
        {
            ".current" => 4L,
            ".last" => 9L,
            _ => null,
        });
        Uri url = new("https://example.com/list?page=1&sort=name");

        string? next = new PageParamPagination().NextPageUrl(page, url, Config());

        Assert.Multiple(() =>
        {
            Assert.That(next, Is.EqualTo("https://example.com/list?page=5&sort=name"));
            Assert.That(next!, Does.Not.Contain("page=1"));
        });
    }

    [TestCase("https://example.com/list?sort=name", "https://example.com/list?sort=name&page=2")]
    [TestCase("https://example.com/list", "https://example.com/list?page=2")]
    public void AbsentParam_Appended(string baseUrl, string expected)
    {
        var page = Page(path => path switch
        {
            ".current" => 1L,
            ".last" => 3L,
            _ => null,
        });

        string? next = new PageParamPagination().NextPageUrl(page, new Uri(baseUrl), Config());

        Assert.That(next, Is.EqualTo(expected));
    }

    [TestCase(5L, 5L)]
    [TestCase(6L, 5L)]
    public void CurrentAtOrBeyondLast_Stops(long current, long last)
    {
        var page = Page(path => path switch
        {
            ".current" => current,
            ".last" => last,
            _ => null,
        });

        Assert.That(new PageParamPagination().NextPageUrl(page, PageUrl, Config()), Is.Null);
    }

    [TestCase(null, 5L)]
    [TestCase("two", 5L)]
    public void MissingOrUncoerciblePageNumbers_Stop(object? current, long last)
    {
        var page = Page(path => path switch
        {
            ".current" => current,
            ".last" => last,
            _ => null,
        });

        Assert.That(new PageParamPagination().NextPageUrl(page, PageUrl, Config()), Is.Null);
    }

    private static ScrapedPage Page(Func<string, object?> readPayloadValue) =>
        new([], Document(), readPayloadValue);

    private static IHtmlDocument Document() => new HtmlParser().ParseDocument("<!doctype html><html></html>");

    private static PaginationConfig Config(string param = "page") =>
        new("page-param", null, param, ".current", ".last");
}
