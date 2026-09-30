using System.Diagnostics;
using Scraper.Core.Interfaces;
using Scraper.Core.Serializers;

namespace Scraper.Core;

public sealed record RunResult(int ItemCount, int PagesScraped, IReadOnlyList<string> FailedUrls)
{
    public bool Success => FailedUrls.Count == 0;
}

public static class ScraperRunner
{
    private const int MaxPages = 50;

    /// <param name="snapshotPath">Optional file for a JSON/Markdown snapshot (format chosen by extension). Null skips the snapshot.</param>
    /// <param name="persist">Called once with all collected items to persist them (e.g. into SQLite).</param>
    public static async Task<RunResult> RunAsync<T>(IScraper<T> scraper, string url, string? snapshotPath, Func<IReadOnlyList<T>, Task> persist)
    {
        var client = new ScrapingClient();
        var allItems = new List<T>();
        var failedUrls = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentUrl = url;
        int pagesScraped = 0;
        var stopwatch = Stopwatch.StartNew();

        while (currentUrl is not null && pagesScraped < MaxPages)
        {
            if (!visited.Add(currentUrl))
            {
                Console.Error.WriteLine($"Pagination loop detected at {currentUrl}; stopping.");
                break;
            }

            string html;
            try
            {
                html = await client.GetHtmlAsync(currentUrl).ConfigureAwait(false);
            }
            catch (RobotsDisallowedException)
            {
                Console.Error.WriteLine($"Stopping at {currentUrl}: disallowed by robots.txt.");
                failedUrls.Add(currentUrl);
                break;
            }
            catch (PageFetchFailedException ex)
            {
                Console.Error.WriteLine($"Stopping at {currentUrl}: {ex.Reason}");
                failedUrls.Add(currentUrl);
                break;
            }
            catch (BrokenCircuitException)
            {
                Console.Error.WriteLine($"Stopping at {currentUrl}: circuit breaker is open for this host.");
                failedUrls.Add(currentUrl);
                break;
            }

            var page = await scraper.ScrapeAsync(html, currentUrl).ConfigureAwait(false);
            allItems.AddRange(page.Items);
            Console.WriteLine($"  page {pagesScraped + 1}: {page.Items.Count} items");

            currentUrl = page.NextPageUrl;
            pagesScraped++;
        }

        stopwatch.Stop();

        await persist(allItems).ConfigureAwait(false);

        string? snapshotFull = null;
        if (snapshotPath is not null)
        {
            var isMarkdown = snapshotPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || snapshotPath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

            string content = isMarkdown
                ? MarkdownSerializer.ToMarkdown(allItems, url)
                : JsonSerializer.ToJson(allItems);

            var fullPath = Path.GetFullPath(snapshotPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmpPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(tmpPath, content).ConfigureAwait(false);
                File.Move(tmpPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }

            snapshotFull = fullPath;
        }

        var summary = $"Done in {stopwatch.Elapsed.TotalSeconds:F1}s: {allItems.Count} items from {pagesScraped} page(s)";
        if (failedUrls.Count > 0)
            summary += $", {failedUrls.Count} failed";
        Console.WriteLine($"{summary} → {SqliteStorage.DatabasePath}");
        if (snapshotFull is not null)
            Console.WriteLine($"Snapshot: {snapshotFull}");

        return new RunResult(allItems.Count, pagesScraped, failedUrls);
    }
}
