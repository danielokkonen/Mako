using System.Diagnostics;
using Mako.Core.Config;
using Mako.Core.Extraction;
using Mako.Core.Interfaces;
using Mako.Core.Serializers;

namespace Mako.Core;

public static class MakoRunner
{
    private const int MaxPages = 50;

    /// <param name="snapshotPath">Optional file for a JSON/Markdown snapshot (format chosen by extension). Null skips the snapshot.</param>
    /// <param name="persist">Called once with all collected items to persist them.</param>
    /// <returns>True when every page succeeded; false if any page failed (the run still persists what was collected).</returns>
    public static async Task<bool> RunAsync(SiteJob job, CapabilityRegistry capabilities, string? snapshotPath, Func<IReadOnlyList<ScrapedItem>, Task> persist)
    {
        if (!capabilities.TryGetSource(job.Source.Kind, out var source))
            throw new ConfigurationException($"Source capability '{job.Source.Kind}' is not registered");

        IPaginationStrategy? pagination = null;
        if (job.Pagination is not null)
        {
            if (!capabilities.TryGetPaginationStrategy(job.Pagination.Strategy, out var strategy))
                throw new ConfigurationException($"Pagination capability '{job.Pagination.Strategy}' is not registered");

            pagination = strategy;
        }

        var client = new ScrapingClient(job.Headers, job.Cookie);
        var allItems = new List<ScrapedItem>();
        var failedUrls = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentUrl = job.Url.ToString();
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

            ScrapedPage page;
            try
            {
                page = await source.ExtractAsync(html, new Uri(currentUrl), job).ConfigureAwait(false);
            }
            catch (PageExtractionException ex)
            {
                Console.Error.WriteLine($"Stopping at {currentUrl}: {ex.Message}");
                failedUrls.Add(currentUrl);
                break;
            }

            allItems.AddRange(page.Items);
            Console.WriteLine($"  page {pagesScraped + 1}: {page.Items.Count} items");

            Uri pageUrl = new(currentUrl);
            currentUrl = pagination is null ? null : pagination.NextPageUrl(page, pageUrl, job.Pagination!);
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
                ? MarkdownSerializer.ToMarkdown(allItems, job.Url.ToString())
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
        Console.WriteLine(summary);
        if (snapshotFull is not null)
            Console.WriteLine($"Snapshot: {snapshotFull}");

        return failedUrls.Count == 0;
    }
}
