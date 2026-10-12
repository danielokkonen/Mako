using Cronos;
using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Mako.Core;
using Mako.Core.Config;
using Mako.Core.Exceptions;
using Mako.Core.Extraction;
using Mako.Core.Interfaces;
using Mako.Core.Pagination;
using Mako.Core.Scheduling;
using Mako.Core.Storage;
using Mako.Core.Transforms;

var configArg = new Argument<string>("config")
{
    HelpName = "CONFIG",
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Path to a job configuration YAML file. Exactly one of CONFIG and --yaml is required.",
};

var yamlOpt = new Option<string>("--yaml")
{
    Arity = ArgumentArity.ExactlyOne,
    Description = "Raw job configuration YAML; alternative to the CONFIG path argument (exactly one of the two is required)."
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

var root = new RootCommand("Mako: config-driven scraping engine — runs one YAML job config, persisting item documents to Postgres with versioned history. Exactly one of the CONFIG path argument and --yaml (raw YAML) is required; the optional OUTPUT arg writes a .json/.md snapshot.")
{
    configArg,
    outputArg,
    yamlOpt,
    scheduleOpt,
};

root.SetAction(async parseResult =>
{
    // Defaults to the local dev Postgres (dev/compose.yaml); prod compose sets MAKO_CONNECTION_STRING.
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

    string? configPath = parseResult.GetValue(configArg);
    string? yamlText = parseResult.GetValue(yamlOpt);
    if (configPath is null == yamlText is null)
    {
        Console.Error.WriteLine("Error: supply exactly one of a CONFIG path or --yaml <raw YAML>.");
        return 1;
    }

    SiteJob job;
    try
    {
        job = configPath is not null ? ConfigLoader.Load(configPath, capabilities) : ConfigLoader.Parse(yamlText!, capabilities);
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
            bool success = await MakoRunner.RunAsync(job, capabilities, output, store.PersistAsync);
            return success ? 0 : 1;
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
