using Scraper.Core.Storage.Models;

namespace Scraper.Core.Storage;

/// <summary>
/// The single EF Core context for the shared SQLite database. All site jobs share this context,
/// so every job's tables (and one shared audit table) live in the same model and migration set.
/// </summary>
public class ScraperDbContext : DbContext
{
    /// <summary>For dependency injection; register via <see cref="ServiceCollectionExtensions.AddScraperStorage"/>.</summary>
    public ScraperDbContext(DbContextOptions<ScraperDbContext> options)
        : base(options)
    {
    }

    /// <summary>Parameterless constructor for EF design-time tooling (migrations); not used at runtime.</summary>
    public ScraperDbContext()
        : this(new DbContextOptionsBuilder<ScraperDbContext>()
            .UseSqlite(SqliteStorage.ConnectionString)
            .Options)
    {
    }

    public DbSet<QuoteRow> Quotes => Set<QuoteRow>();

    public DbSet<QuoteTag> QuoteTags => Set<QuoteTag>();

    public DbSet<BlocketListing> Listings => Set<BlocketListing>();

    public DbSet<AuditEntry> Audits => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<QuoteRow>(e =>
        {
            e.ToTable("quotes");
            e.HasIndex(q => new { q.Text, q.Author }).IsUnique();
        });

        b.Entity<QuoteTag>(e =>
        {
            e.ToTable("quote_tags");
            e.HasKey(t => new { t.QuoteId, t.Tag });
            e.HasOne<QuoteRow>()
                .WithMany(q => q.Tags)
                .HasForeignKey(t => t.QuoteId);
        });

        b.Entity<BlocketListing>(e =>
        {
            e.ToTable("blocket_listings");
            e.HasKey(l => l.Id);
            // Site-provided natural key: never database-generated, so SQLite must not add AUTOINCREMENT.
            e.Property(l => l.Id).ValueGeneratedNever();
        });

        b.Entity<AuditEntry>(e => e.ToTable("audit_entries"));
    }
}
