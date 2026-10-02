using Scraper.Core.Interfaces;

namespace Scraper.Core.Transforms;

/// <summary>
/// Strips leading and trailing curly double-quote characters from text values. The <c>dom</c> source
/// has already trimmed surrounding whitespace, so this only removes the quote marks themselves.
/// Non-string values pass through unchanged.
/// </summary>
public sealed class StripCurlyQuotesTransform : IFieldTransform
{
    public string Name => "strip-curly-quotes";

    public object? Transform(object? value) => value is string s ? s.Trim('“', '”') : value;
}
