# Config-Driven Scraper Engine

## Why

Adding a scraper site today requires a full C# project: hand-written `IScraper<T>` implementation, row entities, EF Core mapping, a store class with bespoke upsert/audit logic, migrations, and ~80 lines of boilerplate `Program.cs`. The goal is for a new job to be **one YAML file**. This also enables a storage redesign (document-first Postgres with per-version history) that supports point-in-time queries, price-trajectory analysis, and LLM-friendly data export — none of which the current typed-entity + before/after-audit design supports well.

## What Changes

- New `Scraper.Engine` library (evolved from core `src/Scraper`) plus a single thin `Scraper.Host` executable; every job is a YAML file under `configs/`.
- **BREAKING**: Remove per-site projects `Scraper.QuotesToscrape` and `Scraper.Blocket`; both sites become config files (`configs/quotes.yaml`, `configs/blocket.yaml`) with behavior preserved.
- **BREAKING**: Drop EF Core, migrations, and typed row entities entirely. Storage becomes Postgres-only, document-first: per-site table `(key TEXT UNIQUE, data JSONB, changed_at)` plus a per-site history table storing one version per change.
- **BREAKING**: Existing SQLite databases are abandoned; no migration path. Dev and prod both use Postgres (local instance via compose for dev).
- New YAML job configuration format: source kind (`dom` / `embedded-json`), field mappings with types and transforms, natural-key declaration, table name, optional request headers/cookie, pagination strategy (`next-link` / `page-param`).
- Change detection moves into the database: `ON CONFLICT` upsert guarded by jsonb semantic equality (`IS DISTINCT FROM`); unchanged rows write nothing.
- Versioned history replaces before/after audit images: first sight of a key inserts version 1; each change appends the next version. Rows and versions are never deleted.
- All strategy fields in config resolve through a DI capability registry (built-ins only in v1) so future C# capabilities can be added without engine or config-syntax changes.
- Compose gains a Postgres service shared by all job services; one Docker image serves every job (different command/env per service); the `/data` named volume is removed.
- Preserved unchanged: robots.txt enforcement, retry/backoff/jitter + per-host circuit breaker, 50-page cap with loop detection, stop-at-first-failed-page semantics, non-zero exit on failure, `--schedule '<cron>'` mode with immediate catch-up run and SIGTERM handling, optional JSON/Markdown snapshot output.

## Capabilities

### New Capabilities

- `job-configuration`: YAML job definition format — required/optional fields, source kinds, field mappings (types, transforms, list fields), natural key, table name, request options, validation and error reporting.
- `data-extraction`: page-to-items behavior for each source kind (`dom` CSS selectors, `embedded-json` marker/base64/JSON path), field type coercion and built-in transforms, and pagination strategies that produce the next-page URL.
- `data-persistence`: document-first Postgres storage — idempotent schema creation, natural-key upsert with jsonb change detection, per-version history appends, never-delete semantics, point-in-time queryability.
- `job-execution`: host CLI contract (config path, optional snapshot output, `--schedule`), run loop behavior (pagination cap, visited-set loop detection, failure handling, exit codes), snapshot serialization, and cron scheduling with graceful shutdown.

### Modified Capabilities

(None — no existing specs; the removed per-site projects had no spec coverage.)

## Impact

- **Code**: `src/Scraper` evolves into the engine library (config loading, extraction capabilities, pagination strategies, storage, runner). New `src/Scraper.Host` exe project. Deleted: both job projects, `Storage/Models/{QuoteRow,QuoteTag,BlocketListing}.cs`, `ScraperDbContext`, `Migrations/`, EF-specific DI registration.
- **Dependencies**: add Npgsql, a YAML parser (YamlDotNet), and a JSONPath library with filter support (candidate selection is a phase-3 spike). Remove `Microsoft.EntityFrameworkCore.*` packages from all projects. Keep AngleSharp, Cronos, System.CommandLine 2.0.12, Polly.
- **Infrastructure**: `compose.yaml` gains a Postgres service; job services share one image with per-service command/env (`TZ`, connection string); `/data` volume removed. Dockerfile builds the host apphost binary (SIGTERM requirement unchanged).
- **Data**: existing SQLite databases at `$XDG_DATA_HOME/Scraper/scraper.db` are abandoned. Fresh Postgres database; dev uses a local Postgres started from the same compose file.
- **Verification**: no test suite (repo convention) — live runs against quotes.toscrape.com and Blocket; pass bar includes idempotency (second run writes zero rows/versions) and history rows appearing only on real change.
