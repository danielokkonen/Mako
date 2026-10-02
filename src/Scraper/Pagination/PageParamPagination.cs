using System.Globalization;
using System.Text;
using Scraper.Core.Config;
using Scraper.Core.Extraction;
using Scraper.Core.Interfaces;

namespace Scraper.Core.Pagination;

/// <summary>
/// The <c>page-param</c> pagination strategy: the current and last page numbers are read from the
/// embedded payload at their declared (anchor-relative) locations, and the next URL is the page
/// URL with the declared query parameter set to current + 1. Missing or uncoercible page numbers
/// mean no next page; current &gt;= last stops pagination.
/// </summary>
public sealed class PageParamPagination : IPaginationStrategy
{
    public string Name => "page-param";

    public string? NextPageUrl(ScrapedPage page, Uri pageUrl, PaginationConfig config)
    {
        if (page.ReadPayloadValue is null ||
            !ToLong(page.ReadPayloadValue(config.Current!), out long current) ||
            !ToLong(page.ReadPayloadValue(config.Last!), out long last))
            return null;

        if (current >= last)
            return null;

        return BuildNextPageUrl(pageUrl, config.Param!, current + 1);
    }

    private static bool ToLong(object? value, out long page)
    {
        switch (value)
        {
            case long l:
                page = l;
                return true;
            case int i:
                page = i;
                return true;
            case double d when Math.Floor(d) == d && !double.IsNaN(d) && !double.IsInfinity(d):
                page = (long)d;
                return true;
            case string s:
                return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
            default:
                page = 0;
                return false;
        }
    }

    /// <summary>
    /// Rebuilds the page URL with <paramref name="param"/> set to <paramref name="nextPage"/>,
    /// preserving every other query parameter (and its order). The first existing occurrence of
    /// the parameter is replaced in place; absent parameters are appended.
    /// </summary>
    private static string BuildNextPageUrl(Uri pageUrl, string param, long nextPage)
    {
        string newValue = $"{param}={nextPage.ToString(CultureInfo.InvariantCulture)}";
        var query = new StringBuilder();
        bool replaced = false;

        foreach (var segment in pageUrl.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = segment.IndexOf('=');
            string key = eq >= 0 ? segment[..eq] : segment;

            if (!replaced && MatchesParam(key, param))
            {
                // Replace the first occurrence in place.
                query.Append(query.Length > 0 ? '&' : string.Empty).Append(newValue);
                replaced = true;
            }
            else
            {
                query.Append(query.Length > 0 ? '&' : string.Empty).Append(segment);
            }
        }

        if (!replaced)
            query.Append(query.Length > 0 ? '&' : string.Empty).Append(newValue);

        return $"{pageUrl.GetLeftPart(UriPartial.Path)}?{query}";
    }

    private static bool MatchesParam(string key, string param)
    {
        if (key == param)
            return true;

        try
        {
            return Uri.UnescapeDataString(key) == param;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            // Malformed percent-encoding: cannot be our parameter.
            return false;
        }
    }
}
