namespace Scraper.Core.Exceptions;

public sealed class PageFetchFailedException : Exception
{
    public string Url { get; }
    public string Reason { get; }

    public PageFetchFailedException(string url, string reason, Exception? inner = null)
        : base($"Failed to fetch {url}: {reason}", inner)
    {
        Url = url;
        Reason = reason;
    }
}
