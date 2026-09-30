namespace Scraper.Core.Exceptions;

public sealed class RobotsDisallowedException : Exception
{
    public string Url { get; }

    public RobotsDisallowedException(string url)
        : base($"URL is disallowed by the site's robots.txt: {url}")
    {
        Url = url;
    }
}
