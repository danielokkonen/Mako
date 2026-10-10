namespace Scraper.Core.Exceptions;

/// <summary>
/// Raised when a fetched page cannot be turned into items: the DOM items selector matched nothing,
/// an embedded payload is missing or malformed, or a natural-key field is missing/uncoercible.
/// The runner treats it like any other failed page (stop, persist what was collected).
/// </summary>
public sealed class PageExtractionException : Exception
{
    public PageExtractionException(string message) : base(message)
    {
    }

    public PageExtractionException(string message, Exception inner) : base(message, inner)
    {
    }
}
