using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scraper.Core.Storage.Models;
using Scraper.Core.Storage;

namespace Scraper.QuotesToscrape;

/// <summary>
/// Persists quotes into the shared SQLite database: upserts on (Text, Author),
/// syncs the quote_tags join table, and appends an audit row for every change.
/// Nothing is ever deleted.
/// </summary>
public sealed class QuotesStore
{
    private readonly ScraperDbContext _db;

    public QuotesStore(ScraperDbContext db) => _db = db;

    public void Initialize()
    {
        SqliteStorage.EnsureCreated();
        _db.Database.Migrate();
    }

    public async Task PersistAsync(IReadOnlyList<Quote> items)
    {
        foreach (var item in items)
        {
            var row = await _db.Quotes
                .Include(q => q.Tags)
                .FirstOrDefaultAsync(q => q.Text == item.Text && q.Author == item.Author)
                .ConfigureAwait(false);

            if (row is null)
            {
                row = new QuoteRow { Text = item.Text, Author = item.Author };
                foreach (var tag in item.Tags)
                    row.Tags.Add(new QuoteTag { Tag = tag });
                _db.Quotes.Add(row);
                _db.Audits.Add(NewAudit(item, "INSERT", null));
            }
            else if (!row.Tags.Select(t => t.Tag).ToHashSet().SetEquals(item.Tags))
            {
                var before = new Quote(row.Text, row.Author, row.Tags.Select(t => t.Tag).ToList());
                _db.QuoteTags.RemoveRange(row.Tags);
                foreach (var tag in item.Tags)
                    row.Tags.Add(new QuoteTag { Tag = tag });
                _db.Audits.Add(NewAudit(item, "UPDATE", AuditJson.Serialize(before)));
            }
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static AuditEntry NewAudit(Quote quote, string action, string? beforeJson) => new()
    {
        EntityKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{quote.Text}\n{quote.Author}"))),
        Action = action,
        ChangedAtUtc = DateTimeOffset.UtcNow,
        BeforeJson = beforeJson,
        AfterJson = AuditJson.Serialize(quote),
    };
}
