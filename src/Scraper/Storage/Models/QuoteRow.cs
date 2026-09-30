namespace Scraper.Core.Storage.Models;

/// <summary>EF entity for the quotes table. Keyed by (Text, Author) via a unique index.</summary>
public class QuoteRow
{
    public int Id { get; set; }

    public string Text { get; set; } = "";

    public string Author { get; set; } = "";

    public List<QuoteTag> Tags { get; set; } = new();
}
