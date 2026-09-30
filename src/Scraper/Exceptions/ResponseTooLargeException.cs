namespace Scraper.Core.Exceptions;

public sealed class ResponseTooLargeException : Exception
{
    public ResponseTooLargeException() : base("Response exceeds the maximum allowed size.") { }
}
