using JsonCons.JsonPath;
using Scraper.Core.Exceptions;

namespace Scraper.Core.Extraction;

/// <summary>
/// Parses the JSON paths used by the <c>embedded-json</c> source and page-param pagination. Config
/// paths may be written relative (leading dot); they are re-rooted at <c>$</c> before evaluation, so
/// a parsed selector is always evaluated against whatever node the caller passes as root.
/// </summary>
internal static class JsonPaths
{
    public static JsonSelector Parse(string path, string what)
    {
        try
        {
            return JsonSelector.Parse(Root(path));
        }
        catch (JsonPathParseException ex)
        {
            throw new ConfigurationException($"Invalid JSON path for {what}: '{path}' ({ex.Message})");
        }
    }

    public static string Root(string path) => path.StartsWith('$') ? path : "$" + path;
}
