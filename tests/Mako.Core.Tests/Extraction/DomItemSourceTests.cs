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

            // Item 3 has no .tag of its own: the list falls back to page level and collects every match there.
            var tags2 = (IReadOnlyList<object?>)page.Items[2].Fields["tags"]!;
            Assert.That(tags2, Is.EqualTo(new object?[] { "a", "b", "c" }));
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
    public async Task AttributeField_ReadsAttributeValueInsteadOfText()
    {
        string html = """
            <html><body>
              <div class="item"><input id="picker" type="date" value="2026-10-13">fallback text</div>
            </body></html>
            """;

        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(DateField()));

        Assert.That(page.Items[0].Fields["date"], Is.EqualTo("2026-10-13"));
    }

    [Test]
    public async Task AttributeField_AbsentAttribute_IsNull()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">t</span><input id="picker" type="date"></div>
            </body></html>
            """;

        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), DateField()));

        Assert.That(page.Items[0].Fields["date"], Is.Null);
    }

    [Test]
    public async Task AttributeField_AbsentElement_TakesDefault()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">t</span></div>
            </body></html>
            """;

        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), DateField("1970-01-01")));

        Assert.That(page.Items[0].Fields["date"], Is.EqualTo("1970-01-01"));
    }

    [Test]
    public async Task ConstantField_TakesCoercedDefaultInEveryItem()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">one</span></div>
              <div class="item"><span class="title">two</span></div>
            </body></html>
            """;

        var constant = new FieldMapping("category", null, FieldType.Integer, null, "42", false);
        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), constant));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items[0].Fields["category"], Is.EqualTo(42));
            Assert.That(page.Items[1].Fields["category"], Is.EqualTo(42));
        });
    }

    [Test]
    public async Task PageLevelSelector_FallsBackToDocument()
    {
        string html = """
            <html><body>
              <input id="picker" type="date" value="2026-10-13">
              <div class="item"><span class="title">one</span></div>
              <div class="item"><span class="title">two</span></div>
            </body></html>
            """;

        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), DateField()));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items[0].Fields["date"], Is.EqualTo("2026-10-13"));
            Assert.That(page.Items[1].Fields["date"], Is.EqualTo("2026-10-13"));
        });
    }

    [Test]
    public async Task ItemLevelMatch_WinsOverDocumentFallback()
    {
        string html = """
            <html><body>
              <span class="meta">page-level</span>
              <div class="item"><span class="title">one</span><span class="meta">in-item-one</span></div>
              <div class="item"><span class="title">two</span><span class="meta">in-item-two</span></div>
            </body></html>
            """;

        var meta = new FieldMapping("meta", ".meta", FieldType.Text, null, null, false);
        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), meta));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items[0].Fields["meta"], Is.EqualTo("in-item-one"));
            Assert.That(page.Items[1].Fields["meta"], Is.EqualTo("in-item-two"));
        });
    }

    [Test]
    public async Task ListFieldWithAttribute_ReadsAttributePerElement_AbsentIsNull()
    {
        string html = """
            <html><body>
              <div class="item"><span class="title">one</span><a class="link" href="/a">A</a><a class="link">B</a></div>
              <div class="item"><span class="title">two</span><a class="link" href="/c">C</a></div>
            </body></html>
            """;

        var links = new FieldMapping("links", ".link", FieldType.Text, null, null, true, "href");
        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), links));

        Assert.Multiple(() =>
        {
            Assert.That((IReadOnlyList<object?>)page.Items[0].Fields["links"]!, Is.EqualTo(new object?[] { "/a", null }));
            Assert.That((IReadOnlyList<object?>)page.Items[1].Fields["links"]!, Is.EqualTo(new object?[] { "/c" }));
        });
    }

    [Test]
    public async Task PageLevelListSelector_FallsBackToDocument()
    {
        string html = """
            <html><body>
              <ul id="nav"><li>one</li><li>two</li></ul>
              <div class="item"><span class="title">t1</span></div>
              <div class="item"><span class="title">t2</span></div>
            </body></html>
            """;

        var links = new FieldMapping("links", "#nav li", FieldType.Text, null, null, true);
        var page = await new DomItemSource([]).ExtractAsync(html, PageUrl, FieldJob(Title(), links));

        Assert.Multiple(() =>
        {
            Assert.That((IReadOnlyList<object?>)page.Items[0].Fields["links"]!, Is.EqualTo(new object?[] { "one", "two" }));
            Assert.That((IReadOnlyList<object?>)page.Items[1].Fields["links"]!, Is.EqualTo(new object?[] { "one", "two" }));
        });
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

    private static SiteJob FieldJob(params FieldMapping[] fields) =>
        new("test", PageUrl, "test_table",
            new SourceConfig("dom", ".item", null, null), fields, [fields[0].Name], null, null, null);

    private static FieldMapping Title() => new("title", ".title", FieldType.Text, null, null, false);

    private static FieldMapping DateField(string? defaultValue = null) =>
        new("date", "#picker", FieldType.Text, null, defaultValue, false, "value");
}
