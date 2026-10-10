namespace Mako.Core.Tests.Extraction;

public class DomItemSourceTests
{
    private static readonly Uri PageUrl = new("https://example.com/list");

    [Test]
    public async Task ItemsAndFields_OneDocumentPerMatch_TrimedScalars_ListInDocumentOrder()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">  First  </span><p class="tag">a</p><p class="tag">b</p></div>
              <div class="item"><span class="title">Second</span><p class="tag">c</p></div>
              <div class="item"><span class="title">Third</span></div>
            </body></html>
            """;

        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, Job());

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Count.EqualTo(3));
            Assert.That(page.Items[0].Fields["title"], Is.EqualTo("First"));
            Assert.That(page.Items[1].Fields["title"], Is.EqualTo("Second"));
            Assert.That(page.Items[2].Fields["title"], Is.EqualTo("Third"));

            var tags0 = (IReadOnlyList<object?>)page.Items[0].Fields["tags"]!;
            var tags1 = (IReadOnlyList<object?>)page.Items[1].Fields["tags"]!;
            Assert.That(tags0, Is.EqualTo(new object?[] { "a", "b" }));
            Assert.That(tags1, Is.EqualTo(new object?[] { "c" }));
            Assert.That(page.Items[2].Fields["tags"], Is.Null);
        });
    }

    [Test]
    public async Task Transform_AppliedToExtractedValues()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">“Hello”</span></div>
            </body></html>
            """;

        var source = new DomItemSource([new StripCurlyQuotesTransform()]);
        var page = await source.ExtractAsync(html, PageUrl, Job(transform: "strip-curly-quotes"));

        Assert.That(page.Items[0].Fields["title"], Is.EqualTo("Hello"));
    }

    [Test]
    public async Task NoMatchOnItemsSelector_ThrowsNamingTheSelector()
    {
        string html = "<html><body><p>nothing matches</p></body></html>";

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new DomItemSource([]).ExtractAsync(html, PageUrl, Job()));

        Assert.That(ex!.Message, Does.Contain("No elements matched items selector").And.Contains(".item"));
    }

    private static SiteJob Job(string? transform = null)
    {
        var title = new FieldMapping("title", ".title", FieldType.Text, transform, null, false);
        var tags = new FieldMapping("tags", ".tag", FieldType.Text, null, null, true);
        return new("test", PageUrl, "test_table",
            new SourceConfig("dom", ".item", null, null), [title, tags], ["title"], null, null, null);
    }
}
