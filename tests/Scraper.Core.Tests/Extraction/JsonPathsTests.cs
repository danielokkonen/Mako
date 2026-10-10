using System.Reflection;

namespace Scraper.Core.Tests.Extraction;

public class JsonPathsTests
{
    private static readonly Type JsonPathsType = typeof(DomItemSource).Assembly.GetType("Scraper.Core.Extraction.JsonPaths")!;

    [TestCase("$foo", "$foo")]
    [TestCase("$.page", "$.page")]
    public void Root_LeadingDollarIsPreserved(string path, string expected) => Assert.That(Root(path), Is.EqualTo(expected));

    [TestCase(".items", "$.items")]
    [TestCase("[0]", "$[0]")]
    public void Root_RelativePathIsReRootedAtDollar(string path, string expected) => Assert.That(Root(path), Is.EqualTo(expected));

    [Test]
    public void Parse_InvalidPathThrowsConfigurationExceptionNamingTheField()
    {
        var ex = Assert.Throws<ConfigurationException>(() => Call("Parse", "$..[]", "anchor"));

        Assert.That(ex!.Message, Does.StartWith("Invalid JSON path for anchor").And.Contains("$..[]"));
    }

    private static string Root(string path) => (string)Call("Root", path)!;

    private static object? Call(string method, params object?[] args)
    {
        try
        {
            return JsonPathsType.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }
}
