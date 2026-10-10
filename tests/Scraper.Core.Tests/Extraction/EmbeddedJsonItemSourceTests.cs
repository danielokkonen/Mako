namespace Scraper.Core.Tests.Extraction;

public class EmbeddedJsonItemSourceTests
{
    private static readonly Uri PageUrl = new("https://example.com/list");

    [Test]
    public async Task HappyPath_MarkerBase64AnchorItems_ScalarsAtRelativePaths()
    {
        string html = Html("""
            {"page": {"items": [{"id": "a", "price": 10}, {"id": "b", "price": 2.5}], "current": 2, "last": 5}}
            """);

        var page = await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id(), Price()]));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Count.EqualTo(2));
            Assert.That(page.Items[0].Fields["id"], Is.EqualTo("a"));
            Assert.That(page.Items[0].Fields["price"], Is.EqualTo(10.0d));
            Assert.That(page.Items[1].Fields["id"], Is.EqualTo("b"));
            Assert.That(page.Items[1].Fields["price"], Is.EqualTo(2.5d));

            Assert.That(page.ReadPayloadValue, Is.Not.Null);
            Assert.That(page.ReadPayloadValue!(".current"), Is.EqualTo(2L));
        });
    }

    [Test]
    public async Task ListField_ArrayVsAbsent()
    {
        string html = Html("""
            {"page": {"items": [{"id": "a", "tags": ["x", "y"]}, {"id": "b"}]}}
            """);

        var page = await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id(), Tags()]));

        Assert.Multiple(() =>
        {
            Assert.That((IReadOnlyList<object?>)page.Items[0].Fields["tags"]!, Is.EqualTo(new object?[] { "x", "y" }));
            Assert.That(page.Items[1].Fields["tags"], Is.Null);
        });
    }

    [TestCase("\"str\"", FieldType.Text, "str")]
    [TestCase("9223372036854775807", FieldType.Long, long.MaxValue)]
    [TestCase("2.5", FieldType.Real, 2.5d)]
    [TestCase("true", FieldType.Text, "True")]
    [TestCase("""{"k": 1}""", FieldType.Text, null)]
    [TestCase("[1, 2]", FieldType.Text, null)]
    [TestCase("null", FieldType.Text, null)]
    public async Task ToPrimitive_MapsStringIntegralDoubleBoolAndNullObjectArray(string jsonValue, FieldType type, object? expected)
    {
        string html = Html($$$"""{"page": {"items": [{"id": "a", "v": {{{jsonValue}}}}]}}""");

        var vField = new FieldMapping("v", ".v", type, null, null, false);
        var page = await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id(), vField]));

        Assert.That(page.Items[0].Fields["v"], Is.EqualTo(expected));
    }

    [Test]
    public async Task MarkerAbsent_ThrowsIdentifyingError()
    {
        string html = "<html><body><script id=\"other\">whatever</script></body></html>";

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()])));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task NoClosingScriptTag_ThrowsIdentifyingError()
    {
        string html = "<html><body><script id=\"data\">" + Base64("""{"page": {"items": []}}""");

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()])));

        Assert.That(ex!.Message, Does.Contain("No closing </script>"));
    }

    [Test]
    public async Task InvalidBase64_ThrowsIdentifyingError()
    {
        string html = "<html><body><script id=\"data\">!!!</script></body></html>";

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()])));

        Assert.That(ex!.Message, Does.Contain("not valid base64"));
    }

    [Test]
    public async Task InvalidJson_ThrowsIdentifyingError()
    {
        string html = "<html><body><script id=\"data\">" + Base64("{not json") + "</script></body></html>";

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()])));

        Assert.That(ex!.Message, Does.Contain("not valid JSON"));
    }

    [Test]
    public async Task AnchorMatchedZeroNodes_ThrowsIdentifyingError()
    {
        string html = Html("""{"page": {"items": []}}""");

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()], anchor: ".nope")));

        Assert.That(ex!.Message, Does.Contain("matched no nodes"));
    }

    [Test]
    public async Task AnchorMatchedMultipleNodes_ThrowsIdentifyingError()
    {
        string html = Html("""[{"i": [1]}, {"i": [2]}]""");

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()], anchor: "$[*]")));

        Assert.That(ex!.Message, Does.Contain("matched 2 nodes"));
    }

    [Test]
    public async Task ItemsPathSelectedZeroArrays_ThrowsIdentifyingError()
    {
        string html = Html("""{"page": {"items": [], "current": 1}}""");

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()], items: ".current")));

        Assert.That(ex!.Message, Does.Contain("selected 0 array(s)"));
    }

    [Test]
    public async Task ItemsPathSelectedMultipleArrays_ThrowsIdentifyingError()
    {
        string html = Html("""{"page": [[1], [2]]}""");

        var ex = Assert.ThrowsAsync<PageExtractionException>(
            async () => await new EmbeddedJsonItemSource([]).ExtractAsync(html, PageUrl, Job(["id"], [Id()], items: "$[*]")));

        Assert.That(ex!.Message, Does.Contain("selected 2 array(s)"));
    }

    private static string Html(string json) =>
        "<html><body><script id=\"data\">" + Base64(json) + "</script></body></html>";

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static SiteJob Job(IReadOnlyList<string> key, FieldMapping[] fields,
        string items = ".items", string anchor = ".page") =>
        new("test", PageUrl, "test_table",
            new SourceConfig("embedded-json", items, "<script id=\"data\">", anchor),
            fields, key, null, null, null);

    private static FieldMapping Id() => new("id", ".id", FieldType.Text, null, null, false);
    private static FieldMapping Price() => new("price", ".price", FieldType.Real, null, null, false);
    private static FieldMapping Tags() => new("tags", ".tags", FieldType.Text, null, null, true);
}
