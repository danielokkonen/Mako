using Npgsql;
using Scraper.Core.Extraction;

namespace Scraper.Core.Storage;

/// <summary>
/// Document-first Postgres storage for one job. Owns two tables in the shared database:
/// a data table (one jsonb document per natural key) and an append-only history table
/// (one full-document version per change, numbered from 1). All SQL is raw ADO.NET over
/// Npgsql — there are no entities to map, so no EF Core. Nothing is ever deleted or
/// overwritten: identical documents write nothing, changed documents update the data row
/// and append the next history version atomically.
/// </summary>
public sealed class DocumentStore
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly string _historyTable;

    public DocumentStore(string connectionString, string table)
    {
        if (!IsValidTableName(table))
            throw new ArgumentException($"Invalid table name '{table}': use a lowercase letter followed by lowercase letters, digits, or underscores.", nameof(table));

        _connectionString = connectionString;
        _table = $"\"{table}\"";
        _historyTable = $"\"{table}_history\"";
    }

    /// <summary>
    /// Creates the job's tables when absent. Safe to run any number of times: existing
    /// schema, rows, and history versions are never altered or dropped.
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand($@"
CREATE TABLE IF NOT EXISTS {_table} (
    key TEXT NOT NULL UNIQUE,
    data JSONB NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS {_historyTable} (
    key TEXT NOT NULL,
    version INT NOT NULL,
    data JSONB NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (key, version)
);", connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Persists one run's items as a single unit: either every upsert and history append
    /// commits, or none do. A document identical to the stored one (ignoring key order
    /// and whitespace) writes nothing — no timestamp bump, no new version.
    /// </summary>
    public async Task PersistAsync(IReadOnlyList<ScrapedItem> items)
    {
        if (items.Count == 0)
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var item in items)
            {
                string json = item.ToJson();

                // Insert or update; the WHERE clause makes identical documents a no-op, and
                // RETURNING yields exactly the keys that were inserted or changed.
                await using var upsert = new NpgsqlCommand($@"
INSERT INTO {_table} AS t (key, data, changed_at) VALUES (@key, @data::jsonb, now())
ON CONFLICT (key) DO UPDATE SET data = excluded.data, changed_at = now()
WHERE excluded.data IS DISTINCT FROM t.data
RETURNING key", connection, transaction);
                upsert.Parameters.AddWithValue("@key", item.Key);
                upsert.Parameters.AddWithValue("@data", json);

                object? result = await upsert.ExecuteScalarAsync();
                if (result is null)
                    continue; // identical document: nothing written

                // First sight of a key gets version 1 (MAX over an empty set is NULL);
                // a changed key appends the next sequential version. Single writer per table,
                // so MAX(version)+1 inside this transaction is safe.
                await using var history = new NpgsqlCommand($@"
INSERT INTO {_historyTable} (key, version, data, changed_at)
VALUES (@key, (SELECT COALESCE(MAX(version), 0) + 1 FROM {_historyTable} WHERE key = @key), @data::jsonb, now())", connection, transaction);
                history.Parameters.AddWithValue("@key", item.Key);
                history.Parameters.AddWithValue("@data", json);
                await history.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>The identifier shape table names must use (also enforced by the config loader).</summary>
    public static bool IsValidTableName(string name) =>
        name.Length is > 0 and <= 63 && char.IsLower(name[0]) && name.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_');
}
