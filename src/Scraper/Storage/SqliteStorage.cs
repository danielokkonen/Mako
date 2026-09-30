namespace Scraper.Core.Storage;

/// <summary>
/// Locates the shared SQLite database in the per-user application data folder:
/// %LOCALAPPDATA%\Scraper\scraper.db on Windows, $XDG_DATA_HOME (or ~/.local/share) on Linux.
/// </summary>
public static class SqliteStorage
{
    public static string DatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scraper",
        "scraper.db");

    public static string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Creates the data directory if it does not exist (SQLite will not create directories).</summary>
    public static void EnsureCreated() => Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
}
