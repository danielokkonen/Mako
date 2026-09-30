using Microsoft.Extensions.DependencyInjection;

namespace Scraper.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the shared <see cref="ScraperDbContext"/> (scoped) against the SQLite database.</summary>
    public static IServiceCollection AddScraperStorage(this IServiceCollection services) =>
        services.AddDbContext<ScraperDbContext>(o => o.UseSqlite(SqliteStorage.ConnectionString));
}
