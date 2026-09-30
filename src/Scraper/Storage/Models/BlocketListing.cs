namespace Scraper.Core.Storage.Models;

/// <summary>EF entity for the blocket_listings table. Keyed by the site-provided natural Id.</summary>
public sealed record BlocketListing(
    long Id,
    string Title,
    int PriceSek,
    string Currency,
    string Location,
    double Latitude,
    double Longitude,
    string Url,
    string ImageUrl,
    string? SellerType,
    string TradeType,
    DateTimeOffset TimestampUtc);
