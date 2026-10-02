using Cronos;
using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Scraper.Core;
using Scraper.Core.Config;
using Scraper.Core.Exceptions;
using Scraper.Core.Extraction;
using Scraper.Core.Interfaces;
using Scraper.Core.Pagination;
using Scraper.Core.Scheduling;
using Scraper.Core.Storage;
using Scraper.Core.Transforms;

var configArg = new Argument<string>("config")
{
    HelpName = "CONFIG",
    Arity = ArgumentArity.ExactlyOne,
};

var outputArg = new Argument<string>("output")
{
    HelpName = "OUTPUT",
    Arity = ArgumentArity.ZeroOrOne
};

var scheduleOpt = new Option<string>("--schedule")
{
    Description = "Cron expression (e.g. '0 */6 * * *'); when set the job runs repeatedly on that schedule instead of exiting."
};

var root = new RootCommand("Config-driven scraper engine: runs one YAML job config, persisting item documents to Postgres with versioned history. Optional OUTPUT arg writes a .json/.md snapshot.")
{
    configArg,
    outputArg,
    scheduleOpt,
};

root.SetAction(async parseResult =>
{
    // Defaults to the local dev Postgres (dev/compose.yaml); prod compose sets SCRAPER_CONNECTION_STRING.
    string connectionString = PostgresStorage.ConnectionString;

    var services = new ServiceCollection();
    services.AddSingleton<IFieldTransform, StripCurlyQuotesTransform>();
    services.AddSingleton<IItemSource, DomItemSource>();
    services.AddSingleton<IItemSource, EmbeddedJsonItemSource>();
    services.AddSingleton<IPaginationStrategy, NextLinkPagination>();
    services.AddSingleton<IPaginationStrategy, PageParamPagination>();
    services.AddSingleton(sp => new CapabilityRegistry(
        sp.GetServices<IItemSource>(),
        sp.GetServices<IPaginationStrategy>(),
        sp.GetServices<IFieldTransform>()));

    using var provider = services.BuildServiceProvider();
    var capabilities = provider.GetRequiredService<CapabilityRegistry>();

    string configPath = parseResult.GetValue(configArg)!;
    SiteJob job;
    try
    {
        job = ConfigLoader.Load(configPath, capabilities);
    }
    catch (ConfigurationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var store = new DocumentStore(connectionString, job.Table);

    Func<CancellationToken, Task<int>> runOnce = async _ =>
    {
        string? output = parseResult.GetValue(outputArg);
        try
        {
            await store.EnsureSchemaAsync();
            Console.WriteLine($"Scraping {job.Url} ...");
            var result = await ScraperRunner.RunAsync(job, capabilities, output, store.PersistAsync);
            return result.Success ? 0 : 1;
        }
        catch (NpgsqlException ex)
        {
            // A dead/unreachable database is a clean error, not a stack trace (schedule mode logs the same).
            Console.Error.WriteLine($"Database error: {ex.Message}");
            return 1;
        }
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
    // ctx.Cancel = true is required: without it the runtime re-applies the signal's default action (death by SIGTERM) after our handler returns.
    using var sigterm = OperatingSystem.IsLinux() ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); }) : null;

    await CronScheduler.RunAsync(schedule, runOnce, cts.Token);
    return 0;
});

return await root.Parse(args).InvokeAsync();
