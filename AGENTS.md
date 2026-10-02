# AGENTS.md

C# config-driven scraping engine. .NET 10, AngleSharp for parsing, no test project and no CI.

## Layout

- `src/Scraper` — **engine library** (namespace `Scraper.Core`): `ScrapingClient` (HTTP + robots.txt check + Polly retry/backoff/jitter + per-host circuit breaker; optional per-job request headers/cookie from config), `RobotsTxtChecker`, the config model + loader in `Config/` (`SiteJob`, field mappings, source kind, pagination strategy — fully validated before any network/DB access), extraction capabilities in `Extraction/` (`DomItemSource`, `EmbeddedJsonItemSource` over JsonCons.JsonPath, `FieldCoercion` to declared types), built-in transforms in `Transforms/`, pagination strategies in `Pagination/` (`NextLinkPagination`, `PageParamPagination`), storage in `Storage/`: `PostgresStorage` (connection string resolution) and `DocumentStore` — raw ADO.NET over Npgsql, **no EF Core**: per-job data table `(key TEXT UNIQUE, data JSONB, changed_at)` + history table `(key, version INT, data JSONB, changed_at)`, PK `(key, version)`; idempotent `CREATE TABLE IF NOT EXISTS`; upsert with jsonb change detection (`IS DISTINCT FROM`); append-only versions. Document-aware serializers in `Serializers/` (`JsonSerializer`, `MarkdownSerializer`), `CronScheduler` (chunked wait so long gaps can't overflow `Task.Delay`), and `ScraperRunner` (fetch → extract → paginate → persist, 50-page cap, visited-set cycle detection, stop-at-first-failed-page + persist-collected).
- `src/Scraper.Host` — **thin exe** (composition root): System.CommandLine 2.0.12 model-based CLI (`<config.yaml> [output] [--schedule '<cron>']`), DI registration of the built-in capabilities by name via `CapabilityRegistry`, Cronos scheduling with immediate catch-up run, SIGTERM/Ctrl+C graceful shutdown.
- `configs/` — **one YAML file per job** (`quotes.yaml`, `blocket.yaml`). A new site = a new YAML file; no C# is written or compiled per site.

## Commands

```bash
dotnet build                                        # builds Scraper.slnx (note: .slnx, not .sln)
dotnet run --project src/Scraper.Host -- configs/<site>.yaml [snapshot.json | snapshot.md] [--schedule '<cron>']
```

- The start URL comes from the config file; there is no central dispatcher. Run a job by pointing the host at its config.
- Data is **always** persisted to the shared Postgres database (see below). The optional 2nd argument additionally writes a JSON/Markdown snapshot file.
- Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker). The runner stops at the first failed page (a failed fetch breaks the pagination chain) but still saves whatever was collected.

### Database / containers

- Connection string resolution: `SCRAPER_CONNECTION_STRING` env var wins; otherwise the local dev default (`Host=localhost;Port=5432;Username=scraper;Password=scraper;Database=scraper`). **Keyword form only** — Npgsql 10.x rejects URI format.
- Local dev Postgres: `podman compose -f dev/compose.yaml up -d db` — **podman is not available in this shell** and cannot be exposed; assume the dev DB is already running (started outside the shell). If a run fails with `Database error:` instead, it isn't.
- Prod: root `compose.yaml` = a `db` service (Postgres, own volume) + one service per config file sharing the `scraper-host` image (per-service command + env: `SCRAPER_CONNECTION_STRING`, `TZ` from `SCRAPER_TZ`). `podman compose up -d --build`.

### Scheduling

- Jobs accept `--schedule '<cron>'` (parsed by Cronos): instead of exiting they loop on that cron schedule until SIGTERM/Ctrl+C; a failed run is logged and retried at the next interval. With the flag the job also runs **once immediately on start** (catch-up for time the machine was off), then fires on each scheduled time. Without the flag: single run, as before.

### Verification (smoke run)

There is no test project; end-to-end verification means running both jobs against the local dev DB (no podman involved — see above):

    dotnet run --project src/Scraper.Host -- configs/quotes.yaml   # static site: expect 10 pages, 100 items
    dotnet run --project src/Scraper.Host -- configs/blocket.yaml  # live listings: page/item counts vary

Success = exit code 0 per job and a `Done in …s: N items from M page(s)` line. A non-zero exit means some page failed — stderr names the URL and reason (site problem, not necessarily a code bug).

## Adding a new site job

1. Write `configs/<site>.yaml`: `name`, `url`, `table` (lowercase identifier; history table is `<table>_history`), `source` (`kind: dom` with CSS `items` selector, or `kind: embedded-json` with `marker:` + JSON-path `anchor:` + relative `items` path), `fields` (each: `name`, `source` — CSS sub-selector for dom / relative JSON path for embedded-json —, `type`: text|integer|long|real|date, optional `transform: strip-curly-quotes`, `default`, `list: true`), `key` (field names forming the natural key), and optionally `pagination` (`strategy: next-link` + `selector`, or `strategy: page-param` + `param`/`current`/`last`), an optional `headers:` map, and an optional `cookie:` with `${VAR}` env interpolation.
2. That's it — the schema is created automatically at job start; nothing to migrate, no C# involved. Optionally add a service entry to the root `compose.yaml` (same image, new command + env).

## Gotchas

- Target framework is **net10.0 deliberately** — this machine only has the .NET 10 SDK/runtime installed. Do not "fix" it to net8.0; the app won't run.
- AngleSharp is **1.x**: parse with `new HtmlParser().ParseDocument(html)` and `GetAttribute(name)` returns `string?`. Older examples using `BrowsingContext.Parse` / string-returning attribute access will not compile.
- The host CLI uses **System.CommandLine 2.0.12**, which exposes the old beta-era model-based API: build a `RootCommand`, set actions with `root.SetAction(parseResult => ...)`, read values via `parseResult.GetValue<T>(arg)`, run with `await root.Parse(args).InvokeAsync()`. The modern `SetHandler`/`command.InvokeAsync` examples from current docs do **not** compile against this version.
- Npgsql connection strings must be **keyword form** (`Host=...;Port=...;Username=...;Password=...;Database=...`). URI format (`postgres://…`) throws `ArgumentException` at parse time (verified on Npgsql 10.0.3).
- SIGTERM graceful shutdown (the `--schedule` loop) only works when running the **apphost binary** (`./Scraper.Host`); via `dotnet <dll>` the muxer intercepts SIGTERM and the process dies without running our handler. The Dockerfile uses the apphost for this reason.
- One job owns its tables: `DocumentStore` computes history versions as `MAX(version)+1` inside the persist transaction, which assumes a single writer per table. Running the same config concurrently (e.g. a manual run while the scheduled container fires) can collide on `(key, version)` and roll back the whole run.

## Conventions

- Pagination: strategies return an absolute next-page URL or null at the last page; the runner follows it, capped at 50 pages per run, with visited-set cycle detection (a repeat URL stops the loop without being a failure).
- Snapshot output format (optional 2nd CLI arg) is chosen by file extension: `.md`/`.markdown` → Markdown (`MarkdownSerializer`, document-aware), anything else → JSON.
- History semantics: first sight of a key inserts version 1; each change appends the next sequential version carrying the full new document, atomically with the data-row update in one transaction (run-level atomicity — failure mid-persist commits nothing). Rows and versions are never deleted or overwritten; point-in-time state = highest version with `changed_at ≤ T`.
- Shared usings are global via `GlobalUsings.cs` — add repeated usings there instead of to individual files.
- `.editorconfig` at the root codifies repo style (UTF-8, LF, 4-space indent, file-scoped namespaces); follow it for new files.
- Root `output.json` / `output.md` are generated data from runs, not source — safe to delete or regenerate.
