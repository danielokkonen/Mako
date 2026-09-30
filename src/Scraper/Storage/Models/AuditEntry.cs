namespace Scraper.Core.Storage.Models;

/// <summary>
/// Append-only audit row recording one change to a scraped entity.
/// All jobs share the single audit_entries table; rows are never updated or deleted.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    /// <summary>Natural key of the entity the change applies to.</summary>
    public string EntityKey { get; set; } = "";

    /// <summary>"INSERT" or "UPDATE".</summary>
    public string Action { get; set; } = "";

    public DateTimeOffset ChangedAtUtc { get; set; }

    /// <summary>Entity state before the change, as JSON. NULL for INSERTs.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Entity state after the change, as JSON.</summary>
    public string AfterJson { get; set; } = "";
}
