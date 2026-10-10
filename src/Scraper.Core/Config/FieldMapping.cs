namespace Scraper.Core.Config;

/// <summary>
/// One field of the stored document. <see cref="Source"/> is a CSS sub-selector for <c>dom</c>
/// sources or an item-relative JSON path for <c>embedded-json</c> sources. <see cref="Default"/>
/// holds the raw YAML value (string/number/bool) and is coerced to <see cref="Type"/> at run time;
/// it applies when extraction yields nothing, as does null when no default is declared.
/// </summary>
public sealed record FieldMapping(string Name, string Source, FieldType Type, string? Transform, object? Default, bool List);
