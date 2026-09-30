# Scraper

A C# HTML scraping framework built on .NET 10 and AngleSharp. A shared core library handles fetching, politeness (robots.txt), resilience, pagination, persistence, and auditing; each target site is a small standalone executable ("job") that plugs into it.

## Features

- **Polite & resilient HTTP client** — robots.txt checks, per-host circuit breaker, Polly retry with backoff/jitter, 1s delay between requests
- **Pagination** — scrapers return an absolute `NextPageUrl`; the runner follows it (capped at 50 pages per run) and stops at the first failed page
- **Persistence** — all jobs share one SQLite database via a single EF Core context; each site gets its own tables plus a shared append-only audit log (`audit_entries`) with before/after JSON images. Rows are upserted by natural key, never deleted
- **Optional snapshots** — any run can additionally write the collected items to a `.json` or `.md` file (format chosen by extension)
- **Scheduling** — jobs accept `--schedule '<cron>'` and loop until SIGTERM/Ctrl+C instead of exiting; with the flag they also run once immediately on start (catch-up), then fire on each scheduled time. Failed runs are logged and retried at the next interval
- **Containerized** — per-project Dockerfiles + a root `compose.yaml`; all services share one named volume so they use a single SQLite DB

## Layout

| Path | What it is |
| --- | --- |
| `src/Scraper` | Core library (`Scraper.Core`): `ScrapingClient`, `RobotsTxtChecker`, `IScraper<T>`, `ScraperRunner`, serializers, EF storage (`SqliteStorage`, `ScraperDbContext`, row entities in `Storage/Models/`), DI extensions. Migrations live here and are applied at startup by each job |
| `src/Scraper.QuotesToscrape` | Job for https://quotes.toscrape.com — scraper + `QuotesStore` (upsert + audit) |
| `src/Scraper.Blocket` | Job for Blocket listings — scraper + `BlocketStore` |

## Requirements

- .NET 10 SDK/runtime (the projects deliberately target **net10.0**)
- Network access to the scraped site(s) for live runs

## Build & run

```bash
dotnet build   # builds Scraper.slnx (note: .slnx, not .sln)

# single run; URL defaults to the site's home page
dotnet run --project src/Scraper.QuotesToscrape -- [url] [snapshot.json | snapshot.md]
```

- Each job is its own executable — there is no central dispatcher. Run a job by project path.
- Data is **always** persisted to the shared SQLite database: `%LOCALAPPDATA%\Scraper\scraper.db` (Windows) or `$XDG_DATA_HOME/Scraper/scraper.db` / `~/.local/share/Scraper/scraper.db` (Linux). The optional 2nd argument additionally writes a snapshot file.
- Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker open). A failed fetch breaks the pagination chain but whatever was collected is still saved.

### Scheduling

```bash
dotnet run --project src/Scraper.QuotesToscrape -- --schedule '0 */6 * * *'   # every 6h on the hour
```

The cron expression is parsed by Cronos; an invalid expression exits with an error before anything runs.

### Containers

Both jobs have a `Dockerfile` (build context = repo root) and are wired up in the root `compose.yaml`:

```bash
podman compose up -d --build   # or: docker compose up -d --build
```

- Both services share the named volume mounted at `/data` (images set `XDG_DATA_HOME=/data`) so they use one SQLite DB.
- Cron time zone comes from the `TZ` env var (`SCRAPER_TZ`, defaults to UTC).
- The Blocket service is currently commented out in `compose.yaml`; uncomment it to enable.

## Adding a new site job

1. Create an exe project `src/Scraper.<SiteName>` referencing `../Scraper/Scraper.csproj`, and add packages: `System.CommandLine`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`.
2. Implement `IScraper<T>` for the site, plus a row entity in core's `Storage/Models/`, its mapping in `ScraperDbContext.OnModelCreating` (natural keys get `.ValueGeneratedNever()`; add a `DbSet<T>`) and a `<Site>Store` (`Initialize()` runs `_db.Database.Migrate()`, `PersistAsync(items)` upserts by natural key + appends an `AuditEntry` per change — see `Scraper.QuotesToscrape/QuotesStore.cs`).
3. Generate the migration in the core project: `dotnet ef migrations add <Name> --project src/Scraper --startup-project src/Scraper.<SiteName>` (requires the global `dotnet-ef` tool; a job project is used as startup because that's where the Design package lives).
4. Give it a small `Program.cs` that parses `[url] [snapshot.json | snapshot.md]`, builds a `ServiceCollection` (`services.AddScraperStorage(); services.AddScoped<YourStore>();`), resolves the store from a scope, calls `store.Initialize()`, then `ScraperRunner.RunAsync(new YourScraper(), url, output, store.PersistAsync)` (see `Scraper.QuotesToscrape/Program.cs`).
5. Add to solution: `dotnet sln Scraper.slnx add src/Scraper.<SiteName>/Scraper.<SiteName>.csproj`.

## Conventions & gotchas

- **No test suite.** Verification is a live run against https://quotes.toscrape.com/ (requires network; the client enforces a 1s polite delay between requests).
- AngleSharp is **1.x**: parse with `new HtmlParser().ParseDocument(html)` and `GetAttribute(name)` returns `string?`.
- Job CLIs use **System.CommandLine 2.0.12** (old beta-era model-based API): build a `RootCommand`, set actions via `root.SetAction(parseResult => ...)`, read values with `parseResult.GetValue<T>(arg)`, run with `await root.Parse(args).InvokeAsync()`.
- EF Core is **10.x**: `Database.Migrate()` is synchronous (returns void); there's a separate `MigrateAsync`.
- Natural-key primary keys must be configured with `.ValueGeneratedNever()`, otherwise SQLite `AUTOINCREMENT` rejects explicit rowid values and breaks out-of-order inserts.
- SIGTERM graceful shutdown in the `--schedule` loop only works when running the **apphost binary** (`./Scraper.<Site>`); via `dotnet <dll>` the muxer intercepts SIGTERM first. Dockerfiles use the apphost for this reason.
- `.editorconfig` at the root codifies repo style (UTF-8, LF, 4-space indent, file-scoped namespaces).
- Root `output.json` / `output.md` are generated data from runs, not source — safe to delete or regenerate.
