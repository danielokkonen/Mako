namespace Mako.Core.Config;

/// <summary>
/// How items are extracted from a page. For <c>dom</c>: <see cref="Items"/> is the CSS selector of
/// each item element and <see cref="Marker"/>/<see cref="Anchor"/> stay null. For <c>embedded-json</c>:
/// <see cref="Marker"/> is the exact script-tag prefix whose base64 content holds the payload,
/// <see cref="Anchor"/> is the JSON path selecting the page-data object, and <see cref="Items"/> is
/// the item-array path relative to that anchor.
/// </summary>
public sealed record SourceConfig(string Kind, string Items, string? Marker, string? Anchor);
