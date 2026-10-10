namespace Scraper.Core.Tests.Config;

public class ConfigLoaderTests
{
    private const string CookieVar = "SCRAPER_CORE_TESTS_COOKIE";

    private CapabilityRegistry _registry;
    private string _dir;
    private string? _savedCookie;

    [SetUp]
    public void SetUp()
    {
        _registry = new CapabilityRegistry(
            [new DomItemSource([]), new EmbeddedJsonItemSource([])],
            [new NextLinkPagination(), new PageParamPagination()],
            [new StripCurlyQuotesTransform()]);

        _dir = Path.Combine("/tmp/opencode", $"scraper-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        _savedCookie = Environment.GetEnvironmentVariable(CookieVar);
        Environment.SetEnvironmentVariable(CookieVar, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);

        Environment.SetEnvironmentVariable(CookieVar, _savedCookie);
    }

    [Test]
    public void ValidMinimalConfig_RoundTripsWithTrimmedNameTableAndParsedUrl()
    {
        // 'table' is validated as an identifier before trimming so it cannot carry padding; 'name' has no shape check and round-trips trimmed.
        var job = Load("""
            name: "  padded  "
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """);

        Assert.Multiple(() =>
        {
            Assert.That(job.Name, Is.EqualTo("padded"));
            Assert.That(job.Table, Is.EqualTo("listings"));
            Assert.That(job.Url, Is.EqualTo(new Uri("https://example.com/list")));
            Assert.That(job.Source.Kind, Is.EqualTo("dom"));
            Assert.That(job.Source.Items, Is.EqualTo(".item"));
            Assert.That(job.Fields, Has.Count.EqualTo(1));
            Assert.That(job.Key, Is.EqualTo(["title"]));
            Assert.That(job.Pagination, Is.Null);
        });
    }

    [Test]
    public void ValidEmbeddedJsonConfig_WithPageParamPagination_Loads()
    {
        var job = Load("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: .page
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            pagination:
              strategy: page-param
              param: page
              current: .current
              last: .last
            """);

        Assert.Multiple(() =>
        {
            Assert.That(job.Source.Kind, Is.EqualTo("embedded-json"));
            Assert.That(job.Pagination!.Strategy, Is.EqualTo("page-param"));
            Assert.That(job.Pagination!.Param, Is.EqualTo("page"));
            Assert.That(job.Pagination!.Current, Is.EqualTo(".current"));
            Assert.That(job.Pagination!.Last, Is.EqualTo(".last"));
        });
    }

    [Test]
    public void MultipleInvalidElements_OneExceptionListingAllErrors()
    {
        var ex = ExpectInvalid("""
            url: not-a-url
            table: Bad-Table
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """,
            "'name' is required and must be a non-empty string",
            "is not an absolute http(s) URL",
            "is not a valid identifier");

        Assert.That(ex.Message, Does.StartWith("Invalid configuration in"));
    }

    [Test]
    public void BlankName_RejectedNamingTheElement()
        => ExpectInvalid("""
            name: "   "
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "'name' is required and must be a non-empty string");

    [Test]
    public void MissingUrl_RejectedNamingTheElement()
        => ExpectInvalid("""
            name: test job
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "'url' is required (absolute http(s) start URL)");

    [Test]
    public void NonHttpSchemeUrl_Rejected()
        => ExpectInvalid("""
            name: test job
            url: ftp://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "is not an absolute http(s) URL");

    [Test]
    public void RelativeUrl_Rejected()
        => ExpectInvalid("""
            name: test job
            url: /list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "is not an absolute http(s) URL");

    [Test]
    public void InvalidTableIdentifier_RejectedNamingTheValue()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: Listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "'table' value 'Listings' is not a valid identifier");

    [Test]
    public void UnregisteredSourceKind_RejectedNamingTheRegisteredSet()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: html
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """,
            "source kind 'html' is not registered (registered: dom, embedded-json)");

    [Test]
    public void DomItemsInvalidCss_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: 'div['
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            """, "'source.items' value 'div[' is not a valid CSS selector");

    [Test]
    public void EmbeddedJsonMissingMarkerAndAnchor_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            """,
            "'source.marker' is required for 'embedded-json' sources",
            "'source.anchor' is required for 'embedded-json' sources");

    [Test]
    public void EmbeddedJsonInvalidAnchorPath_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: '$['
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            """, "Invalid JSON path for 'source.anchor'");

    [Test]
    public void EmbeddedJsonInvalidItemsPath_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: .page
              items: '$['
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            """, "Invalid JSON path for 'source.items'");

    [Test]
    public void EmptyFieldsList_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields: []
            key:
              - title
            """, "'fields' must declare at least one field mapping");

    [Test]
    public void DuplicateFieldNames_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
              - name: title
                source: .other
                type: text
            key:
              - title
            """, "field 'title': field names must be unique");

    [Test]
    public void UnsupportedFieldType_RejectedNamingTheSupportedSet()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: float
            key:
              - title
            """, "type 'float' is not supported (supported: text | integer | long | real | date)");

    [Test]
    public void UnknownTransform_RejectedNamingTheRegisteredSet()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
                transform: shout
            key:
              - title
            """, "transform 'shout' is not registered (registered: strip-curly-quotes)");

    [Test]
    public void KeyReferencingUndeclaredField_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - nope
            """, "key references 'nope' which is not a declared field name");

    [Test]
    public void ListFieldInKey_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
              - name: tags
                source: .tag
                type: text
                list: true
            key:
              - title
              - tags
            """, "key field 'tags' cannot be marked as a list field");

    [Test]
    public void NextLinkWithoutSelector_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            pagination:
              strategy: next-link
            """, "'pagination.selector' is required for the 'next-link' strategy");

    [Test]
    public void NextLinkInvalidCssSelector_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            pagination:
              strategy: next-link
              selector: 'div['
            """, "pagination selector 'div[' is not a valid CSS selector");

    [Test]
    public void PageParamWithDomSource_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            pagination:
              strategy: page-param
              param: page
              current: .current
              last: .last
            """, "the 'page-param' strategy requires an 'embedded-json' source");

    [Test]
    public void PageParamMissingCurrentAndLast_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: .page
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            pagination:
              strategy: page-param
              param: page
            """,
            "'pagination.current' is required for the 'page-param' strategy",
            "'pagination.last' is required for the 'page-param' strategy");

    [Test]
    public void PageParamMissingParam_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: .page
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            pagination:
              strategy: page-param
              current: .current
              last: .last
            """,
            "'pagination.param' is required for the 'page-param' strategy");

    [Test]
    public void PageParamInvalidCurrentPath_Rejected()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: embedded-json
              marker: '<script id="data">'
              anchor: .page
              items: .items
            fields:
              - name: id
                source: .id
                type: text
            key:
              - id
            pagination:
              strategy: page-param
              param: page
              current: '$['
              last: .last
            """, "Invalid JSON path for 'pagination.current'");

    [Test]
    public void UnknownPaginationStrategy_RejectedNamingIt()
        => ExpectInvalid("""
            name: test job
            url: https://example.com/list
            table: listings
            source:
              kind: dom
              items: .item
            fields:
              - name: title
                source: .title
                type: text
            key:
              - title
            pagination:
              strategy: skip
            """, "pagination strategy 'skip' is not registered (registered: next-link, page-param)");

    [Test]
    public void CookieUnsetEnvVar_FailsNamingTheVariable()
    {
        ExpectInvalid(ValidDomYaml(CookieLine()), $"cookie references environment variable '{CookieVar}' which is not set");
    }

    [Test]
    public void CookieSetEnvVar_IsInterpolatedIntoSiteJob()
    {
        Environment.SetEnvironmentVariable(CookieVar, "secret123");

        var job = Load(ValidDomYaml(CookieLine()));

        Assert.That(job.Cookie, Is.EqualTo("token=secret123"));
    }

    private static string ValidDomYaml(string? extraLine = null) => """
        name: test job
        url: https://example.com/list
        table: listings
        source:
          kind: dom
          items: .item
        fields:
          - name: title
            source: .title
            type: text
        key:
          - title
        """ + (extraLine is null ? "" : "\n" + extraLine);

    // Built by concatenation on purpose: in an interpolated raw string "${expr}" would not yield the literal braces this YAML needs.
    private static string CookieLine() => "cookie: \"token=${" + CookieVar + "}\"\n";

    private SiteJob Load(string yaml)
    {
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        return ConfigLoader.Load(path, _registry);
    }

    private ConfigurationException ExpectInvalid(string yaml, params string[] fragments)
    {
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);

        var ex = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(path, _registry));
        foreach (string fragment in fragments)
            Assert.That(ex!.Message, Does.Contain(fragment), $"expected the error to mention: {fragment}");

        return ex;
    }
}
