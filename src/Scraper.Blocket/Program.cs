using Cronos;
using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Scraper.Core;
using Scraper.Blocket;
using Scraper.Core.Scheduling;
using Scraper.Core.DependencyInjection;

var urlArg = new Argument<string>("url")
{
    HelpName = "URL",
    Arity = ArgumentArity.ZeroOrOne,
    DefaultValueFactory = _ => "https://www.blocket.se/recommerce/forsale/search?consoles_type=16&product_category=2.93.3905.63"
};

var outputArg = new Argument<string>("output")
{
    HelpName = "OUTPUT",
    Arity = ArgumentArity.ZeroOrOne
};

var scheduleOpt = new Option<string>("--schedule")
{
    Description = "Cron expression (e.g. '0 6 * * *'); when set the job runs repeatedly on that schedule instead of exiting."
};

var root = new RootCommand("Scrape blocket.se search results; persists to SQLite with an audit log. Optional OUTPUT arg writes a .json/.md snapshot.")
{
    urlArg,
    outputArg,
    scheduleOpt,
};

root.SetAction(async parseResult =>
{
    var url = parseResult.GetValue(urlArg);
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
    {
        Console.Error.WriteLine($"Error: '{url}' is not a valid http(s) URL.");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddScraperStorage();
    services.AddScoped<BlocketStore>();
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<BlocketStore>();
    store.Initialize();

    Func<CancellationToken, Task<int>> runOnce = async _ =>
    {
        string? output = parseResult.GetValue(outputArg);
        Console.WriteLine($"Scraping {uri} ...");
        var result = await ScraperRunner.RunAsync(new BlocketScraper(), uri.ToString(), output, store.PersistAsync);
        return result.Success ? 0 : 1;
    };

    string? schedule = parseResult.GetValue(scheduleOpt);
    if (schedule is null)
    {
        return await runOnce(CancellationToken.None);
    }

    try
    {
        CronExpression.Parse(schedule);
    }
    catch (FormatException ex)
    {
        Console.Error.WriteLine($"Error: '{schedule}' is not a valid cron expression ({ex.Message}).");
        return 1;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { cts.Cancel(); e.Cancel = true; };
    using var sigterm = OperatingSystem.IsLinux() ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => cts.Cancel()) : null;

    await CronScheduler.RunAsync(schedule, runOnce, cts.Token);
    return 0;
});

return await root.Parse(args).InvokeAsync();
