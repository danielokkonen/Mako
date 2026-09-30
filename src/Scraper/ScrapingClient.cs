using Polly;
using Polly.Retry;
using Scraper.Core.Helpers;

namespace Scraper.Core;

public sealed class ScrapingClient
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan PoliteDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private const long MaxResponseBytes = 10 * 1024 * 1024;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly RobotsTxtChecker _robots = new();
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _pipelinesByHost = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    static ScrapingClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
    }

    public async Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        var uri = new Uri(url);

        if (!await _robots.IsAllowedAsync(url, ct).ConfigureAwait(false))
            throw new RobotsDisallowedException(url);

        await WaitPoliteDelayAsync(ct).ConfigureAwait(false);

        var pipeline = _pipelinesByHost.GetOrAdd(uri.Authority, static _ => BuildPipeline());

        HttpResponseMessage response;
        try
        {
            response = await pipeline.ExecuteAsync<HttpResponseMessage>((CancellationToken token) => FetchCoreAsync(url, token), ct).ConfigureAwait(false);
        }
        catch (ScrapeTimeoutException ex)
        {
            throw new PageFetchFailedException(url, $"timed out after {RequestTimeout.TotalSeconds:0} s", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PageFetchFailedException(url, ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new PageFetchFailedException(url, $"network error: {ex.Message}", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 400)
                throw new PageFetchFailedException(url, $"HTTP {(int)response.StatusCode}");

            try
            {
                return await ReadBodyAsync(response.Content, ct).ConfigureAwait(false);
            }
            catch (ResponseTooLargeException ex)
            {
                throw new PageFetchFailedException(url, "response exceeds the 10 MB size limit", ex);
            }
        }
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline()
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 1.0,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(60),
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome)),
            OnOpened = _ =>
            {
                Console.Error.WriteLine("Circuit breaker opened: host is failing repeatedly; pausing requests for 60 s.");
                return default;
            },
        });

        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = BaseBackoff,
            MaxDelay = MaxBackoff,
            ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
            DelayGenerator = args =>
            {
                var retryAfter = args.Outcome.Result?.Headers.RetryAfter;
                TimeSpan? delta = retryAfter?.Delta ?? (retryAfter is not null ? retryAfter.Date - DateTime.UtcNow : null);
                if (delta is { } d && d > TimeSpan.Zero)
                    return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromTicks(Math.Min(d.Ticks, MaxBackoff.Ticks)));

                return ValueTask.FromResult<TimeSpan?>(null);
            },
        });

        return builder.Build();
    }

    private static async ValueTask<HttpResponseMessage> FetchCoreAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        try
        {
            return await Http.GetAsync(url, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ScrapeTimeoutException($"The request timed out after {RequestTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        int read;

        while ((read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes)
                throw new ResponseTooLargeException();
            buffer.Write(chunk, 0, read);
        }

        var encoding = content.Headers.ContentType?.CharSet is { Length: > 0 } charset
            ? Encoding.GetEncoding(charset)
            : Encoding.UTF8;
        return encoding.GetString(buffer.ToArray());
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is IOException or ScrapeTimeoutException || IsRetryableStatus(outcome.Result);

    private static bool IsFailure(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is not null and not OperationCanceledException
        || (outcome.Result is not null && (int)outcome.Result.StatusCode >= 400);

    private static bool IsRetryableStatus(HttpResponseMessage? response) =>
        response is not null && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);

    private async Task WaitPoliteDelayAsync(CancellationToken ct)
    {
        var elapsed = DateTime.UtcNow - _lastRequestUtc;
        if (elapsed < PoliteDelay)
            await Task.Delay(PoliteDelay - elapsed, ct).ConfigureAwait(false);
        _lastRequestUtc = DateTime.UtcNow;
    }
}
