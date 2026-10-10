namespace Mako.Core.Exceptions;

public sealed class ScrapeTimeoutException : Exception
{
    public ScrapeTimeoutException(string message) : base(message) { }
}
