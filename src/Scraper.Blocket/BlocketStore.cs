using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scraper.Core.Storage.Models;
using Scraper.Core.Storage;

namespace Scraper.Blocket;

/// <summary>
/// Persists Blocket listings into the shared SQLite database: upserts on the site's natural Id,
/// and appends an audit row for every change. Nothing is ever deleted.
/// </summary>
public sealed class BlocketStore
{
    private readonly ScraperDbContext _db;

    public BlocketStore(ScraperDbContext db) => _db = db;

    public void Initialize()
    {
        SqliteStorage.EnsureCreated();
        _db.Database.Migrate();
    }

    public async Task PersistAsync(IReadOnlyList<BlocketListing> items)
    {
        foreach (var item in items)
        {
            var existing = await _db.Listings.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == item.Id)
                .ConfigureAwait(false);

            if (existing is null)
            {
                _db.Listings.Add(item);
                _db.Audits.Add(NewAudit(item, "INSERT", null));
            }
            else if (!existing.Equals(item))
            {
                var beforeJson = AuditJson.Serialize(existing);
                _db.Listings.Attach(item);
                _db.Entry(item).State = EntityState.Modified;
                _db.Audits.Add(NewAudit(item, "UPDATE", beforeJson));
            }
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static AuditEntry NewAudit(BlocketListing listing, string action, string? beforeJson) => new()
    {
        EntityKey = listing.Id.ToString(CultureInfo.InvariantCulture),
        Action = action,
        ChangedAtUtc = DateTimeOffset.UtcNow,
        BeforeJson = beforeJson,
        AfterJson = AuditJson.Serialize(listing),
    };
}
