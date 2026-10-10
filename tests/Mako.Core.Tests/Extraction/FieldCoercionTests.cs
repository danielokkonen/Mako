using System.Globalization;

namespace Mako.Core.Tests.Extraction;

public class FieldCoercionTests
{
    [Test]
    public void Text_PassesStringThrough()
        => Assert.That(FieldCoercion.Coerce("abc", FieldType.Text), Is.EqualTo("abc"));

    [TestCase(42L, "42")]
    [TestCase(true, "True")]
    [TestCase(1.5d, "1.5")]
    public void Text_RendersNonStringInvariant(object? raw, string expected)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Text), Is.EqualTo(expected));

    [TestCase("42", 42)]
    [TestCase(42L, 42)]
    [TestCase(42, 42)]
    [TestCase(42.0d, 42)]
    public void Integer_FromStringLongIntAndIntegralDouble(object? raw, int expected)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Integer), Is.EqualTo(expected));

    [TestCase(2147483648L)]
    [TestCase("-2147483649")]
    [TestCase("99999999999")]
    [TestCase(42.5d)]
    public void Integer_OutOfRangeOrUncoercible_IsNull(object? raw)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Integer), Is.Null);

    [TestCase("9223372036854775807", 9223372036854775807L)]
    [TestCase(42L, 42L)]
    public void Long_FromStringAndLong(object? raw, long expected)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Long), Is.EqualTo(expected));

    [TestCase("abc")]
    [TestCase(1.5d)]
    public void Long_Uncoercible_IsNull(object? raw)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Long), Is.Null);

    [TestCase("1.5", 1.5d)]
    [TestCase(42, 42.0d)]
    [TestCase(1.5d, 1.5d)]
    public void Real_FromStringIntAndDouble(object? raw, double expected)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Real), Is.EqualTo(expected));

    [Test]
    public void Real_Uncoercible_IsNull()
        => Assert.That(FieldCoercion.Coerce("abc", FieldType.Real), Is.Null);

    [Test]
    public void Date_FromIsoWithoutOffset_AssumedUtc()
    {
        object? result = FieldCoercion.Coerce("2026-01-02T03:04:05", FieldType.Date);

        Assert.That(result, Is.EqualTo(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
    }

    [TestCase(1700000000000L)]
    [TestCase("1700000000000")]
    public void Date_FromUnixEpochMilliseconds(object? raw)
        => Assert.That(FieldCoercion.Coerce(raw, FieldType.Date),
            Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000)));

    [Test]
    public void Date_Uncoercible_IsNull()
        => Assert.That(FieldCoercion.Coerce("not-a-date", FieldType.Date), Is.Null);

    [Test]
    public void BuildItem_ScalarPresent_IsCoerced()
    {
        var job = Job(["title"], Title(), Count());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1"), ("count", "5")), job, 1);

        Assert.That(item.Fields["title"], Is.EqualTo("t1"));
        Assert.That(item.Fields["count"], Is.EqualTo(5));
    }

    [Test]
    public void BuildItem_ScalarAbsentWithDefault_TakesCoercedDefault()
    {
        var job = Job(["title"], Title(), Count());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1")), job, 1);

        Assert.That(item.Fields["count"], Is.EqualTo(7));
    }

    [Test]
    public void BuildItem_ScalarUncoercibleWithDefault_TakesCoercedDefault()
    {
        var job = Job(["title"], Title(), Count());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1"), ("count", "abc")), job, 1);

        Assert.That(item.Fields["count"], Is.EqualTo(7));
    }

    [Test]
    public void BuildItem_ScalarAbsentWithoutDefault_IsNull()
    {
        var job = Job(["title"], Title(), Price());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1")), job, 1);

        Assert.That(item.Fields["price"], Is.Null);
    }

    [Test]
    public void BuildItem_MissingKeyField_ThrowsNamingPositionAndField()
    {
        var job = Job(["id"], Id());

        var ex = Assert.Throws<PageExtractionException>(() => FieldCoercion.BuildItem(Raw(), job, 2));

        Assert.That(ex!.Message, Does.Contain("item 2").And.Contains("'id'"));
    }

    [Test]
    public void BuildItem_UncoercibleKeyField_ThrowsNamingPositionAndField()
    {
        var job = Job(["id"], Id());

        var ex = Assert.Throws<PageExtractionException>(() => FieldCoercion.BuildItem(Raw(("id", "abc")), job, 3));

        Assert.That(ex!.Message, Does.Contain("item 3").And.Contains("'id'"));
    }

    [Test]
    public void BuildItem_ListField_Matches_CoercedElementList()
    {
        var job = Job(["title"], Title(), Tags());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1"), ("tags", new object?[] { "10", "20" })), job, 1);

        Assert.That(item.Fields["tags"], Is.InstanceOf<IReadOnlyList<object?>>());
        var tags = (IReadOnlyList<object?>)item.Fields["tags"]!;
        Assert.Multiple(() =>
        {
            Assert.That(tags.Count, Is.EqualTo(2));
            Assert.That(tags[0], Is.EqualTo(10));
            Assert.That(tags[1], Is.EqualTo(20));
        });
    }

    [Test]
    public void BuildItem_ListField_Absent_IsNull()
    {
        var job = Job(["title"], Title(), Tags());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1")), job, 1);

        Assert.That(item.Fields["tags"], Is.Null);
    }

    [Test]
    public void BuildItem_ListField_NonListValue_IsNull()
    {
        var job = Job(["title"], Title(), Tags());

        var item = FieldCoercion.BuildItem(Raw(("title", "t1"), ("tags", "not-a-list")), job, 1);

        Assert.That(item.Fields["tags"], Is.Null);
    }

    [Test]
    public void BuildItem_NaturalKey_JoinsValuesWithUnitSeparator_AndFormatsDateAsO()
    {
        var idField = new FieldMapping("id", ".id", FieldType.Text, null, null, false);
        var job = Job(["id", "ts"], idField, Ts());

        var item = FieldCoercion.BuildItem(Raw(("id", "abc"), ("ts", "2026-01-02T03:04:05")), job, 1);

        Assert.That(FieldCoercion.KeySeparator, Is.EqualTo("\u001F"));

        Assert.That(item.Key, Is.EqualTo("abc\u001F2026-01-02T03:04:05.0000000+00:00"));
    }

    private static SiteJob Job(IReadOnlyList<string> key, params FieldMapping[] fields) =>
        new("test", new Uri("https://example.com/"), "test_table",
            new SourceConfig("dom", ".item", null, null), fields, key, null, null, null);

    private static Dictionary<string, object?> Raw(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Name, f => f.Value);

    private static FieldMapping Title() => new("title", ".title", FieldType.Text, null, null, false);
    private static FieldMapping Count() => new("count", ".count", FieldType.Integer, null, 7L, false);
    private static FieldMapping Price() => new("price", ".price", FieldType.Real, null, null, false);
    private static FieldMapping Id() => new("id", ".id", FieldType.Integer, null, null, false);
    private static FieldMapping Ts() => new("ts", ".ts", FieldType.Date, null, null, false);
    private static FieldMapping Tags() => new("tags", ".tag", FieldType.Integer, null, null, true);
}
