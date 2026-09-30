# AGENTS.md

C# HTML scraper framework. .NET 10, AngleSharp for parsing, no test project and no CI.

## Layout

- `src/Scraper` — **core library** (namespace `Scraper.Core`): `ScrapingClient` (HTTP + robots.txt check + Polly retry/backoff/jitter + per-host circuit breaker), `RobotsTxtChecker`, `IScraper<T>`, `ScraperRunner` (fetch → scrape → paginate → persist, optional snapshot file). Serializers live in `Serializers/`: `MarkdownSerializer`, `JsonSerializer`. Persistence lives in `Storage/`: `SqliteStorage` (shared DB path/connection string), `ScraperDbContext` (the **single** EF Core context for all jobs — every site's tables plus one shared `audit_entries` table; migrations live in `src/Scraper/Migrations/`, applied at startup by each job), the row entities in `Storage/Models/` (`QuoteRow`, `QuoteTag`, `BlocketListing`, plus the append-only `AuditEntry`), and `AuditJson` (before/after image serializer). DI registration lives in `DependencyInjection/`: `ServiceCollectionExtensions.AddScraperStorage()` registers the context as scoped; jobs inject it. Shared usings are global via `GlobalUsings.cs` — add repeated usings there instead of to individual files.
- `src/Scraper.<SiteName>` — one **exe project per site/job**, each referencing the core library and owning a `<Site>Store` (upsert + audit logic, called by the runner as the persist callback) plus its scraper. All EF persistence is shared via core's `ScraperDbContext`. Currently: `Scraper.QuotesToscrape`, `Scraper.Blocket`.

## Commands

```bash
dotnet build                                        # builds Scraper.slnx (note: .slnx, not .sln)
dotnet run --project src/Scraper.QuotesToscrape -- [url] [snapshot.json | snapshot.md]   # 2nd arg optional
```

- Each job is its own executable — there is no central dispatcher. Run a job by project path; `url` defaults to the site's home page.
- Data is **always** persisted to the shared SQLite database at `%LOCALAPPDATA%\Scraper\scraper.db` (Windows) / `$XDG_DATA_HOME/Scraper/scraper.db` or `~/.local/share/Scraper/scraper.db` (Linux). The optional 2nd arg additionally writes a JSON/Markdown snapshot file.
- Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker). The runner stops at the first failed page (a failed fetch breaks the pagination chain) but still saves whatever was collected.

### Scheduling / containers

- Both jobs accept `--schedule '<cron>'` (parsed by Cronos): instead of exiting they loop on that cron schedule until SIGTERM/Ctrl+C; a failed run is logged and retried at the next interval. With the flag the job also runs **once immediately on start** (catch-up for time the machine was off), then fires on each scheduled time. Without the flag: single run, as before.
- Containerized via per-project Dockerfiles (`src/Scraper.<Site>/Dockerfile`, build context = repo root) + root `compose.yaml`: `podman compose up -d --build`. Both services share a named volume mounted at `/data` (images set `XDG_DATA_HOME=/data`) so they use one SQLite DB; cron time zone comes from the `TZ` env var (`SCRAPER_TZ`).
- There is no test suite. Verification = a live run against https://quotes.toscrape.com/ (requires network; the client enforces a 1s polite delay between requests).

## Adding a new site job

1. Create an exe project `src/Scraper.<SiteName>` referencing `../Scraper/Scraper.csproj`, and add packages: `System.CommandLine`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (`dotnet add src/Scraper.<SiteName> package <name>`).
2. Implement `IScraper<T>` for the site in that project (namespace `Scraper.<SiteName>`), plus a row entity in core's `Storage/Models/`, its mapping in `ScraperDbContext.OnModelCreating` (natural keys get `.ValueGeneratedNever()`; add a `DbSet<T>` property to the context) and a `<Site>Store` (`Initialize()` runs `_db.Database.Migrate()`, `PersistAsync(items)` upserts by natural key + appends an `AuditEntry` per change — see `Scraper.QuotesToscrape/QuotesStore.cs`).
3. Generate the migration in the core project: `dotnet ef migrations add <Name> --project src/Scraper --startup-project src/Scraper.<SiteName>` (requires the global `dotnet-ef` tool; a job project is used as startup because that's where the Design package lives).
4. Give it a small `Program.cs` that parses `[url] [snapshot.json | snapshot.md]` with System.CommandLine, builds a `ServiceCollection` (`services.AddScraperStorage(); services.AddScoped<YourStore>();`), resolves the store from a scope (`provider.CreateScope()` — the scoped context is disposed with the scope; stores take the context via constructor and are **not** IDisposable), calls `store.Initialize()`, then `ScraperRunner.RunAsync(new YourScraper(), url, output, store.PersistAsync)` (see `Scraper.QuotesToscrape/Program.cs`).
5. Add to solution: `dotnet sln Scraper.slnx add src/Scraper.<SiteName>/Scraper.<SiteName>.csproj`.

## Gotchas

- Target framework is **net10.0 deliberately** — this machine only has the .NET 10 SDK/runtime installed. Do not "fix" it to net8.0; the app won't run.
- AngleSharp is **1.x**: parse with `new HtmlParser().ParseDocument(html)` and `GetAttribute(name)` returns `string?`. Older examples using `BrowsingContext.Parse` / string-returning attribute access will not compile.
- Job CLIs use **System.CommandLine 2.0.12**, which exposes the old beta-era model-based API: build a `RootCommand`, set actions with `root.SetAction(parseResult => ...)`, read values via `parseResult.GetValue<T>(arg)`, run with `await root.Parse(args).InvokeAsync()`. The modern `SetHandler`/`command.InvokeAsync` examples from current docs do **not** compile against this version.
- EF Core is **10.x**: `Database.Migrate()` is **synchronous (returns void)** — there is a separate `MigrateAsync`. Chaining `.ConfigureAwait(false)` on it will not compile.
- Natural-key primary keys must be configured with `.ValueGeneratedNever()`, otherwise EF adds SQLite `AUTOINCREMENT`, which rejects explicit rowid values lower than the historical max and breaks out-of-order inserts.
- SIGTERM graceful shutdown (the `--schedule` loop) only works when running the **apphost binary** (`./Scraper.<Site>`); via `dotnet <dll>` the muxer intercepts SIGTERM and the process dies without running our handler. Dockerfiles use the apphost for this reason.

## Conventions

- Pagination: scrapers return an absolute `NextPageUrl` (resolve relative hrefs against `baseUrl`); the runner follows it, capped at 50 pages per run.
- Snapshot output format (optional 2nd CLI arg) is chosen by file extension: `.md`/`.markdown` → Markdown (`MarkdownSerializer`, reflection over item properties), anything else → JSON.
- Audit log semantics: stores upsert on the natural key and append one `AuditEntry` per change (INSERT or UPDATE, with before/after JSON images). Re-running on unchanged data writes nothing; rows are never deleted from main tables or audit tables.

## Files

- `.editorconfig` at the root codifies repo style (UTF-8, LF, 4-space indent, file-scoped namespaces); follow it for new files.
- Root `output.json` / `output.md` are generated data from runs, not source — safe to delete or regenerate.
- `.gitignore` comes from `dotnet new gitignore`; it does not ignore the generated output files.
