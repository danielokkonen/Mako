using Cronos;

namespace Scraper.Core.Scheduling;

public static class CronScheduler
{
    public static async Task RunAsync(string cronExpression, Func<CancellationToken, Task<int>> runOnce, CancellationToken cancellationToken)
    {
        var expression = CronExpression.Parse(cronExpression);

        // Runs once immediately (so a machine that was off comes back with fresh data), then follows the schedule.
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[schedule] Running scrape ...");
            try
            {
                var exitCode = await runOnce(cancellationToken);
                Console.WriteLine(exitCode == 0 ? "[schedule] Run succeeded." : $"[schedule] Run failed (exit code {exitCode}); will retry on the next interval.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[schedule] Run threw an exception: {ex.Message}; will retry on the next interval.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var next = expression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local, inclusive: false)
                       ?? throw new InvalidOperationException($"No future occurrence found for '{cronExpression}'.");
            Console.WriteLine($"[schedule] Next run at {next.LocalDateTime:yyyy-MM-dd HH:mm:ss} ({cronExpression})");

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await Task.Delay(next - DateTimeOffset.Now, waitCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        Console.WriteLine("[schedule] Stopped.");
    }
}
