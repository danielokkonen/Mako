namespace Scraper.Core.Tests.Storage;

public class DocumentStoreTableNameTests
{
    [TestCaseSource(nameof(ValidIdentifiers))]
    public void ValidIdentifiers_AreAccepted(string name) => Assert.That(DocumentStore.IsValidTableName(name), Is.True);

    [TestCaseSource(nameof(InvalidIdentifiers))]
    public void InvalidIdentifiers_AreRejected(string name) => Assert.That(DocumentStore.IsValidTableName(name), Is.False);

    // TestCaseSource (not literal [TestCase]) because the max-length cases are not compile-time constants.

    private static IEnumerable<string> ValidIdentifiers()
    {
        yield return "listings";
        yield return "a";
        yield return "a1_b2";
        yield return new string('a', 63);
    }

    private static IEnumerable<string> InvalidIdentifiers()
    {
        yield return "";
        yield return "Listings";
        yield return "list-ings";
        yield return new string('a', 64);
    }
}
