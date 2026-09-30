using System.Text.Json;
using System.Web;
using Scraper.Core.Interfaces;
using Scraper.Core.Storage.Models;

namespace Scraper.Blocket;

public sealed class BlocketScraper : IScraper<BlocketListing>
{
    private const string ScriptMarker = "<script type=\"application/json\" data-react-query-state>";

    public Task<ScrapePageResult<BlocketListing>> ScrapeAsync(string html, string baseUrl)
        => Task.FromResult(Scrape(html, baseUrl));

    private static ScrapePageResult<BlocketListing> Scrape(string html, string baseUrl)
    {
        var payload = ExtractPayload(html);

        using var doc = JsonDocument.Parse(payload);
        JsonElement? search = null;
        foreach (var query in doc.RootElement.GetProperty("queries").EnumerateArray())
        {
            if (!query.TryGetProperty("state", out var state) ||
                !state.TryGetProperty("data", out var data))
                continue;

            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("docs", out _))
            {
                search = data;
                break;
            }
        }

        if (search is null)
            throw new InvalidOperationException("No search results found in page payload.");

        var items = search.Value.GetProperty("docs")
            .EnumerateArray()
            .Select(ParseListing)
            .ToList();

        string? next = null;
        if (search.Value.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("paging", out var paging))
        {
            int current = paging.GetProperty("current").GetInt32();
            int last = paging.GetProperty("last").GetInt32();
            if (current < last)
                next = BuildPageUrl(baseUrl, paging.GetProperty("param").GetString() ?? "page", current + 1);
        }

        return new ScrapePageResult<BlocketListing>(items, next);
    }

    private static BlocketListing ParseListing(JsonElement d)
    {
        string Title(string name) => d.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

        (double Lat, double Lon) = (0, 0);
        if (d.TryGetProperty("coordinates", out var coords))
        {
            Lat = coords.TryGetProperty("lat", out var lat) ? lat.GetDouble() : 0;
            Lon = coords.TryGetProperty("lon", out var lon) ? lon.GetDouble() : 0;
        }

        string? sellerType = null;
        if (d.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array && flags.GetArrayLength() > 0)
            sellerType = flags[0].GetString();

        long timestampMs = d.TryGetProperty("timestamp", out var ts) ? ts.GetInt64() : 0;

        return new BlocketListing(
            Id: long.Parse(d.GetProperty("id").GetString()!),
            Title: Title("heading"),
            PriceSek: d.GetProperty("price").GetProperty("amount").GetInt32(),
            Currency: d.GetProperty("price").GetProperty("currency_code").GetString() ?? "SEK",
            Location: Title("location"),
            Latitude: Lat,
            Longitude: Lon,
            Url: d.TryGetProperty("canonical_url", out var url) ? url.GetString()! : "",
            ImageUrl: d.TryGetProperty("image", out var image) && image.TryGetProperty("url", out var img)
                ? img.GetString() ?? ""
                : "",
            SellerType: sellerType,
            TradeType: Title("trade_type"),
            TimestampUtc: DateTimeOffset.FromUnixTimeMilliseconds(timestampMs));
    }

    private static byte[] ExtractPayload(string html)
    {
        int start = html.IndexOf(ScriptMarker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("Search payload script tag not found.");

        start += ScriptMarker.Length;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException("Search payload script tag is malformed.");

        string b64 = html[start..end].Trim();
        return Convert.FromBase64String(b64);
    }

    private static string BuildPageUrl(string baseUrl, string pageParam, int page)
    {
        var uri = new Uri(baseUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query[pageParam] = page.ToString();
        return $"{uri.GetLeftPart(UriPartial.Path)}?{query}";
    }
}
