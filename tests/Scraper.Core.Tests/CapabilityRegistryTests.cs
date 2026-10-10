namespace Scraper.Core.Tests;

public class CapabilityRegistryTests
{
    [Test]
    public void CaseInsensitiveLookup_ReturnsTheRegisteredCapability()
    {
        var source = new FakeSource("my-source");
        var strategy = new FakeStrategy("next-link-x");
        var transform = new FakeTransform("strip-curly-quotes");
        var registry = new CapabilityRegistry([source], [strategy], [transform]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGetSource("MY-SOURCE", out var s), Is.True);
            Assert.That(s, Is.SameAs(source));
            Assert.That(registry.TryGetPaginationStrategy("NEXT-LINK-X", out var p), Is.True);
            Assert.That(p, Is.SameAs(strategy));
            Assert.That(registry.TryGetTransform("STRIP-CURLY-QUOTES", out var t), Is.True);
            Assert.That(t, Is.SameAs(transform));
        });
    }

    [Test]
    public void UnknownName_ReturnsFalseWithNull()
    {
        var registry = new CapabilityRegistry([new FakeSource("a")], [new FakeStrategy("b")], [new FakeTransform("c")]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGetSource("nope", out var s), Is.False);
            Assert.That(s, Is.Null);
            Assert.That(registry.TryGetPaginationStrategy("nope", out var p), Is.False);
            Assert.That(p, Is.Null);
            Assert.That(registry.TryGetTransform("nope", out var t), Is.False);
            Assert.That(t, Is.Null);
        });
    }

    [Test]
    public void RegisteredStrings_AreSortedAndJoined()
    {
        var registry = new CapabilityRegistry(
            [new FakeSource("zeta"), new FakeSource("alpha"), new FakeSource("mid")],
            [new FakeStrategy("b-strat"), new FakeStrategy("a-strat")],
            [new FakeTransform("t-two"), new FakeTransform("t-one")]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.RegisteredSources, Is.EqualTo("alpha, mid, zeta"));
            Assert.That(registry.RegisteredPaginationStrategies, Is.EqualTo("a-strat, b-strat"));
            Assert.That(registry.RegisteredTransforms, Is.EqualTo("t-one, t-two"));
        });
    }

    private sealed class FakeSource(string name) : IItemSource
    {
        public string Name => name;
        public Task<ScrapedPage> ExtractAsync(string html, Uri pageUrl, SiteJob job) => throw new NotSupportedException();
    }

    private sealed class FakeStrategy(string name) : IPaginationStrategy
    {
        public string Name => name;
        public string? NextPageUrl(ScrapedPage page, Uri pageUrl, PaginationConfig config) => null;
    }

    private sealed class FakeTransform(string name) : IFieldTransform
    {
        public string Name => name;
        public object? Transform(object? value) => value;
    }
}
