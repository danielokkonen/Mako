namespace Mako.Core.Tests.Transforms;

public class NormalizeWhitespaceTransformTests
{
    private readonly NormalizeWhitespaceTransform Transform = new();

    [Test]
    public void Name_IsTheRegistrationKey()
        => Assert.That(Transform.Name, Is.EqualTo("normalize-whitespace"));

    [TestCase("00:00 -\n                    00:15", "00:00 - 00:15")]
    [TestCase("  padded\ttext \t ", "padded text")]
    [TestCase("already clean", "already clean")]
    public void CollapsesWhitespaceRunsToSingleSpaceAndTrims(string input, string expected) =>
        Assert.That(Transform.Transform(input), Is.EqualTo(expected));

    [TestCase(42L, 42L)]
    [TestCase(true, true)]
    [TestCase(null, null)]
    public void NonStringValuesPassThroughUnchanged(object? value, object? expected) =>
        Assert.That(Transform.Transform(value), Is.EqualTo(expected));
}
