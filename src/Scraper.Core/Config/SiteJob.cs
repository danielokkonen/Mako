namespace Scraper.Core.Config;

/// <summary>
/// A fully validated scraper job, loaded from one YAML file. Everything the engine needs to run:
/// target site, extraction rules, natural key, storage table, and optional request options.
/// </summary>
public sealed record SiteJob(
    string Name,
    Uri Url,
    string Table,
    SourceConfig Source,
    IReadOnlyList<FieldMapping> Fields,
    IReadOnlyList<string> Key,
    PaginationConfig? Pagination,
    IReadOnlyDictionary<string, string>? Headers,
    string? Cookie);
