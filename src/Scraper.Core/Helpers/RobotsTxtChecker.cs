namespace Scraper.Core.Helpers;

public sealed class RobotsTxtChecker
{
    private const long MaxRobotsBytes = 1_048_576;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ConcurrentDictionary<string, Task<RobotRules>> _rulesByHost = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsAllowedAsync(string url, CancellationToken ct = default)
    {
        var uri = new Uri(url);
        var rules = await _rulesByHost.GetOrAdd(uri.Authority, _ => LoadRulesAsync(uri)).ConfigureAwait(false);
        return rules.IsAllowed(uri);
    }

    private static async Task<RobotRules> LoadRulesAsync(Uri url)
    {
        var robotsUrl = new Uri(url.Scheme + "://" + url.Authority + "/robots.txt");
        try
        {
            using var response = await Http.GetAsync(robotsUrl).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return RobotRules.AllowAll;

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Warning: robots.txt for {url.Host} returned HTTP {(int)response.StatusCode}; allowing all paths.");
                return RobotRules.AllowAll;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length > MaxRobotsBytes)
            {
                Console.Error.WriteLine($"Warning: robots.txt for {url.Host} is larger than 1 MB; allowing all paths.");
                return RobotRules.AllowAll;
            }

            return RobotRules.Parse(Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Warning: could not fetch robots.txt for {url.Host} ({ex.Message}); allowing all paths.");
            return RobotRules.AllowAll;
        }
    }
}

public sealed class RobotRules
{
    public static readonly RobotRules AllowAll = new(Array.Empty<RobotGroup>());

    private readonly RobotGroup[] _groups;

    private RobotRules(RobotGroup[] groups) => _groups = groups;

    public static RobotRules Parse(string text)
    {
        var groups = new List<RobotGroup>();
        RobotGroup? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash].Trim();
            if (line.Length == 0)
                continue;

            var sep = line.IndexOf(':');
            if (sep <= 0)
                continue;

            var field = line[..sep].Trim().ToLowerInvariant();
            var value = line[(sep + 1)..].Trim();

            switch (field)
            {
                case "user-agent":
                    if (current is null || current.Rules.Count > 0)
                        current = AddGroup(groups);
                    current.Agents.Add(value);
                    break;

                case "allow" or "disallow":
                    if (value.Length == 0)
                        break;
                    current ??= AddGroup(groups);
                    current.Rules.Add(new RobotRule(value, field == "allow"));
                    break;
            }
        }

        return new RobotRules(groups.ToArray());
    }

    public bool IsAllowed(Uri url)
    {
        var target = url.AbsolutePath + url.Query;

        RobotRule? best = null;
        int bestLength = -1;

        foreach (var group in _groups)
        {
            if (!group.MatchesGenericCrawler())
                continue;

            foreach (var rule in group.Rules)
            {
                if (!target.StartsWith(rule.Path, StringComparison.Ordinal))
                    continue;

                var length = rule.Path.Length;
                if (length > bestLength || (length == bestLength && !rule.IsAllow))
                {
                    best = rule;
                    bestLength = length;
                }
            }
        }

        return best?.IsAllow ?? true;
    }

    private static RobotGroup AddGroup(List<RobotGroup> groups)
    {
        var group = new RobotGroup();
        groups.Add(group);
        return group;
    }
}

internal sealed class RobotGroup
{
    public List<string> Agents { get; } = new();
    public List<RobotRule> Rules { get; } = new();

    public bool MatchesGenericCrawler() =>
        Agents.Count == 0 || Agents.Contains("*", StringComparer.OrdinalIgnoreCase);
}

internal sealed record RobotRule(string Path, bool IsAllow);
