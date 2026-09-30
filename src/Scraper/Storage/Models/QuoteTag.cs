namespace Scraper.Core.Storage.Models;

/// <summary>Join table between quotes and their tags. Keyed by (QuoteId, Tag).</summary>
public class QuoteTag
{
    public int QuoteId { get; set; }

    public string Tag { get; set; } = "";
}
