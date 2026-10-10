namespace Mako.Core.Tests.Pagination;

public class NextLinkPaginationTests
{
    private static readonly Uri PageUrl = new("https://example.com/list?page=1");

    [Test]
    public void AbsoluteHref_ReturnedAsIs()
    {
        var page = Page("<html><body><a class=\"next\" href=\"https://example.com/page2\">Next</a></body></html>");

        string? next = new NextLinkPagination().NextPageUrl(page, PageUrl, Config());

        Assert.That(next, Is.EqualTo("https://example.com/page2"));
    }

    [Test]
    public void RelativeHref_ResolvedAgainstPageUrl()
    {
        var page = Page("<html><body><a class=\"next\" href=\"/page2\">Next</a></body></html>");

        string? next = new NextLinkPagination().NextPageUrl(page, PageUrl, Config());

        Assert.That(next, Is.EqualTo("https://example.com/page2"));
    }

    [Test]
    public void NoMatch_Stops()
    {
        var page = Page("<html><body><p>no link here</p></body></html>");

        string? next = new NextLinkPagination().NextPageUrl(page, PageUrl, Config());

        Assert.That(next, Is.Null);
    }

    [TestCase("<html><body><a class=\"next\"></a></body></html>")]
    [TestCase("<html><body><a class=\"next\" href=\"\">Next</a></body></html>")]
    [TestCase("<html><body><a class=\"next\" href=\"   \">Next</a></body></html>")]
    public void EmptyOrWhitespaceHref_Stops(string html)
    {
        var page = Page(html);

        Assert.That(new NextLinkPagination().NextPageUrl(page, PageUrl, Config()), Is.Null);
    }

    [Test]
    public void UnusableHref_ThrowsUriFormatException_Stops()
    {
        var page = Page("<html><body><a class=\"next\" href=\"http://example.com:99999/\">Next</a></body></html>");

        string? next = new NextLinkPagination().NextPageUrl(page, PageUrl, Config());

        Assert.That(next, Is.Null);
    }

    private static ScrapedPage Page(string html) =>
        new([], new HtmlParser().ParseDocument(html), null);

    private static PaginationConfig Config(string selector = "a.next") =>
        new("next-link", selector, null, null, null);
}
