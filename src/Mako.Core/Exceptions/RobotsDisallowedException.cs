namespace Mako.Core.Exceptions;

public sealed class RobotsDisallowedException : Exception
{
    public RobotsDisallowedException(string url)
        : base($"URL is disallowed by the site's robots.txt: {url}")
    {
    }
}
