namespace Scraper.Core.Storage;

/// <summary>
/// Resolves the connection string for the shared Postgres database. Local development defaults
/// to this machine's own server (dev/compose.yaml); production sets SCRAPER_CONNECTION_STRING
/// explicitly in compose, which always wins over the default.
/// </summary>
public static class PostgresStorage
{
    public const string EnvironmentVariable = "SCRAPER_CONNECTION_STRING";

    /// <summary>The local dev database: `podman compose -f dev/compose.yaml up -d db`.</summary>
    public const string LocalDevConnectionString = "Host=localhost;Port=5432;Username=user;Password=password;Database=scraper";

    public static string ConnectionString
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? LocalDevConnectionString : configured!;
        }
    }
}
