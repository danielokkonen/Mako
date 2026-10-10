# Mako

A config-driven scraping engine built on .NET 10 and AngleSharp. A shared core library handles fetching, politeness (robots.txt), resilience, extraction, pagination, persistence, and scheduling; each target site is a single YAML file under `configs/` — no C# per site.

## Features

- **Polite & resilient HTTP client** — robots.txt checks, per-host circuit breaker, Polly retry with backoff/jitter, 1s delay between requests
- **Config-driven jobs** — one YAML file per site: source kind (`dom` CSS selectors or `embedded-json` marker/base64/JSON path), typed field mappings, natural key, pagination strategy; the whole config is validated before any network or DB access
- **Pagination** — `next-link` (CSS selector → absolute href) or `page-param` (current/last read from page data); the runner follows until exhausted (capped at 50 pages per run) and stops at the first failed page
- **Persistence** — all jobs share one Postgres database; each site gets a data table (one JSONB document per natural key) plus an append-only history table (one full-document version per change). Identical documents write nothing; rows and versions are never deleted
- **Optional snapshots** — any run can additionally write the collected items to a `.json` or `.md` file (format chosen by extension)
- **Scheduling** — jobs accept `--schedule '<cron>'` and loop until SIGTERM/Ctrl+C instead of exiting; with the flag they also run once immediately on start (catch-up), then fire on each scheduled time. Failed runs are logged and retried at the next interval
- **Containerized** — one Docker image serves every job; `compose.yaml` wires a Postgres service plus one service per config file

## Layout

| Path | What it is |
| --- | --- |
| `src/Mako.Core` | Engine library (`Mako.Core`): `ScrapingClient`, `RobotsTxtChecker`, config model + loader, extraction capabilities (`dom`, `embedded-json`), field coercion and transforms, pagination strategies, `DocumentStore` (raw ADO.NET over Npgsql — no EF Core), serializers, `CronScheduler`, `MakoRunner` |
| `src/Mako.Host` | Thin executable: CLI parsing, DI composition root registering the built-in capabilities by name, cron scheduling, SIGTERM handling |
| `configs/quotes.yaml` | Job for https://quotes.toscrape.com (dom source, next-link pagination) |
| `configs/blocket.yaml` | Job for Blocket listings (embedded-json source, page-param pagination) |
| `tests/Mako.Core.Tests` | NUnit unit tests mirroring the production folder structure (`Config/`, `Extraction/`, `Pagination/`, `Storage/`, `Transforms/`) — no Postgres or network required |

## Requirements

- .NET 10 SDK/runtime (the projects deliberately target **net10.0**)
- A Postgres database (local dev: `podman compose -f dev/compose.yaml up -d db`)
- Network access to the scraped site(s) for live runs

## Build & run

```bash
dotnet build   # builds Mako.slnx (note: .slnx, not .sln)
dotnet test    # runs the unit tests (tests/Mako.Core.Tests)

# single run; the start URL comes from the config file
dotnet run --project src/Mako.Host -- configs/quotes.yaml [snapshot.json | snapshot.md] [--schedule '<cron>']
```

- There is no central dispatcher — you run a job by pointing the host at its config file.
- Data is **always** persisted to the shared Postgres database (connection string resolution below). The optional 2nd argument additionally writes a snapshot file.
- Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker open). A failed fetch breaks the pagination chain but whatever was collected is still saved.

### Database connection

- Local runs default to this machine's Postgres (`Host=localhost;Port=5432;Username=user;Password=password;Database=mako`) — start it with `podman compose -f dev/compose.yaml up -d db`. No environment variable needed for local development.
- Override with the `MAKO_CONNECTION_STRING` env var. **Keyword form only** (`Host=db;Port=5432;Username=...;Password=...;Database=mako`) — Npgsql 10.x rejects URI format (`postgres://…`). The prod `compose.yaml` sets it explicitly per service, pointing at its own Postgres container.

### Scheduling

```bash
dotnet run --project src/Mako.Host -- configs/quotes.yaml --schedule '0 */6 * * *'   # every 6h on the hour
```

The cron expression is parsed by Cronos; an invalid expression exits with an error before anything runs. With `--schedule` the job also runs once immediately on start (catch-up for time the machine was off).

### Containers

One Dockerfile (`src/Mako.Host/Dockerfile`, build context = repo root) builds a single image that serves every job; the root `compose.yaml` wires it up:

```bash
podman compose up -d --build   # or: docker compose up -d --build
```

- A `db` service (Postgres with its own volume) plus one service per config file — same image, different command (`configs/x.yaml`) and env.
- Cron time zone comes from the `TZ` env var (`MAKO_TZ`, defaults to UTC).

## Adding a new site job

Write one YAML file under `configs/` — no C# is written or compiled per site:

```yaml
name: My Site
url: https://example.com/start
table: my_site            # lowercase identifier; the history table is <table>_history
source:
  kind: dom               # dom | embedded-json
  items: div.item         # CSS selector of each item element (dom) / relative JSON path to the item array (embedded-json)
fields:
  - name: title           # type: text | integer | long | real | date; optional transform, default, list: true
    source: .title        # CSS sub-selector (dom) or relative JSON path (embedded-json)
    type: text
key: [title]              # natural key = the declared key fields combined
pagination:               # omit for a single-page job
  strategy: next-link     # next-link (selector:) | page-param (param:/current:/last:)
  selector: li.next a
headers:                  # optional, sent with every page request
  X-Api-Key: "..."
cookie: "session=${SESSION}"   # optional; ${VAR} is read from the environment at run time (fail-fast if unset)
```

For `embedded-json`, add `marker:` (the exact script-tag prefix whose base64 content holds the payload) and `anchor:` (a JSON path selecting the page-data object). The schema for both tables is created automatically at job start — nothing to migrate. Optionally, add a service entry to `compose.yaml` (same image, new command + env).
