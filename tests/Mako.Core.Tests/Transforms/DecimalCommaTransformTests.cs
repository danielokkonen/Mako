namespace Mako.Core.Tests.Transforms;

public class DecimalCommaTransformTests
{
    private readonly DecimalCommaTransform Transform = new();

    [Test]
    public void Name_IsTheRegistrationKey()
        => Assert.That(Transform.Name, Is.EqualTo("decimal-comma"));

    [TestCase("1,43", "1.43")]
    [TestCase("1234,56", "1234.56")]
    [TestCase("no comma here", "no comma here")]
    public void ReplacesDecimalCommasWithPoints(string input, string expected) =>
        Assert.That(Transform.Transform(input), Is.EqualTo(expected));

    [TestCase(42L, 42L)]
    [TestCase(true, true)]
    [TestCase(null, null)]
    public void NonStringValuesPassThroughUnchanged(object? value, object? expected) =>
        Assert.That(Transform.Transform(value), Is.EqualTo(expected));
}
