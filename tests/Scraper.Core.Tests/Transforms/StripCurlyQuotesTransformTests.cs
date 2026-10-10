namespace Scraper.Core.Tests.Transforms;

public class StripCurlyQuotesTransformTests
{
    private readonly StripCurlyQuotesTransform Transform = new();

    [Test]
    public void Name_IsTheRegistrationKey()
    {
        Assert.That(Transform.Name, Is.EqualTo("strip-curly-quotes"));
    }

    [TestCase("“Hello”", "Hello")]
    [TestCase("“one-sided", "one-sided")]
    [TestCase("trailing-only”", "trailing-only")]
    [TestCase("no quotes here", "no quotes here")]
    public void StripsLeadingAndTrailingCurlyQuotes(string input, string expected) =>
        Assert.That(Transform.Transform(input), Is.EqualTo(expected));

    [TestCase(42L, 42L)]
    [TestCase(true, true)]
    [TestCase(null, null)]
    public void NonStringValuesPassThroughUnchanged(object? value, object? expected) =>
        Assert.That(Transform.Transform(value), Is.EqualTo(expected));
}
