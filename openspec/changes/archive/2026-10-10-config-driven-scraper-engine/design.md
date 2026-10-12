# Design: Config-Driven Scraper Engine

## Context

Current state: core library `src/Scraper` (namespace `Scraper.Core`) provides `ScrapingClient` (HTTP + robots.txt check + Polly retry/backoff/jitter + per-host circuit breaker), `ScraperRunner` (fetch → scrape → paginate → persist, 50-page cap, visited-set loop detection, stop-at-first-failed-page, optional snapshot file), and EF Core persistence (`ScraperDbContext`, one shared SQLite database, typed entities per site, migrations). Each site is an exe project owning an `IScraper<T>` implementation, a store class (upsert + before/after audit images), row entities, and ~80 lines of near-identical `Program.cs` (System.CommandLine 2.0.12 beta API, Cronos scheduling, SIGTERM handling).

Hard constraints carried into this design: target framework **net10.0** (only SDK installed); AngleSharp **1.x** (`new HtmlParser().ParseDocument`, `GetAttribute` returns `string?`); System.CommandLine **2.0.12** model-based API only; apphost binary required for SIGTERM to reach our handler; no test suite — verification is live runs.

See proposal.md for motivation and scope.

## Goals / Non-Goals

**Goals:**
- A new job = one YAML file under `configs/`; zero C# written or compiled per site.
- Document-first Postgres storage: jsonb document as canonical record, natural-key upsert, append-only per-version history, never-delete semantics, point-in-time recoverability.
- One engine library + one host executable; one Docker image for every job.
- Pre-shaped extensibility: all config strategy fields resolve through a DI capability registry so future C# capabilities need no engine or config-syntax change.

**Non-Goals (v1):**
- Typed projection columns and expression indexes over jsonb (deferred; single-dialect now, cheap to add later).
- Any plugin-loading machinery (DLL discovery, scripting in YAML) — the registry seam exists, no custom capabilities ship.
- Multi-format config (JSON/TOML) — YAML only.
- History retention/compaction policy.
- Data migration from existing SQLite databases — they are abandoned by decision.

## Decisions

### D1: Engine library + thin host executable; strategies resolved by name via DI
`src/Scraper` evolves into the engine **library** (config loading, extraction, pagination, storage, runner). A new `src/Scraper.Host` exe is a thin composition root: parses CLI, builds the service collection registering built-in capabilities, runs. Every strategy field in config (`source`, `pagination.strategy`, transforms) resolves through a name→capability registry backed by DI.

- *Alternatives considered:* (a) keep per-site projects — rejected, that's the status quo this change removes; (b) engine loads plugin DLLs from a directory at runtime — rejected, assembly loading outside a normal app is where dependency/version problems live and it's unneeded until a real capability appears; (c) expression/scripting language in YAML — rejected, an embedded language is its own project.
- *Why this shape:* the registry exists whether or not we call it a plugin system (config already names strategies), so "add C# later" becomes: implement interface, reference project from host, register. No engine change, no config-syntax change.

### D2: YAML-only configuration format
One parser (YamlDotNet), one `SiteJob` model, validated before any network or DB access. JSON/TOML support deliberately deferred — the model serializes to JSON for free if ever needed.

- *Alternative considered:* accept all three formats by file extension from day one — rejected; triple the validation/error-message surface for a use case that hasn't appeared.

### D3: Postgres-only, document-first storage; EF Core removed entirely
Per-site data table `(key TEXT UNIQUE, data JSONB, changed_at timestamptz)` plus per-site history table `(key, version INT, data JSONB, changed_at timestamptz)`, PK `(key, version)`. Schema created idempotently at startup (`CREATE TABLE IF NOT EXISTS`). All SQL is raw ADO.NET over Npgsql — **no EF Core anywhere**: no entities to map (rows are documents), so the context, migrations folder, and Design package all disappear.

- *Alternatives considered:* (a) typed tables via EF migrations per site — rejected, incompatible with runtime config; (b) dual-provider SQLite-dev/Postgres-prod with a dialect shim — **rejected by decision**: user will run a local Postgres for dev, which deletes the shim, the `SCRAPER_DB` switch, and the dev/prod fidelity gap entirely.
- *Why drop EF:* it existed to map typed entities; document-first storage has none. Keeping it would mean a context with zero mapped entities doing raw SQL through ceremony.

### D4: Change detection in SQL via jsonb semantic equality
Upsert is one statement: `INSERT … ON CONFLICT (key) DO UPDATE SET data = excluded.data, changed_at = now() WHERE excluded.data IS DISTINCT FROM <table>.data RETURNING key`. Returned keys are exactly the inserted/changed ones; unchanged documents write nothing.

- *Why:* jsonb equality ignores key order and whitespace, so detection is correct by construction in-database. This replaces the app-side canonicalized-JSON hashing that was required when SQLite stored JSON as opaque TEXT — that whole mechanism no longer exists.
- *Alternative considered:* fetch current rows, compare in app — rejected; more round-trips and re-introduces normalization logic.

### D5: Per-site versioned history replaces before/after audit images
First sight of a key inserts data row + history v1 (one transaction); each change updates the data row and appends the next sequential version carrying the full new document. No action labels stored — derivable (v1 = insert). Nothing is ever deleted or overwritten.

- *Why versions over before/after pairs:* each version stored once instead of twice; point-in-time and trajectory queries ("price history for listing X", "table state as of date T") become clean range queries over one table.
- *Alternative considered:* Postgres 14+ native system-versioned tables (`_hist`) — rejected: they capture UPDATE/DELETE but **not INSERT**, so v1-at-first-sight would require an explicit history write anyway, leaving a hidden table on top for no benefit; also GUC-dependent and invisible to normal DDL.
- *Version numbering:* `MAX(version)+1` per key inside the persist transaction — safe because exactly one job owns each table (single writer).

### D6: Extraction as named capabilities over two source kinds
`dom`: AngleSharp parse, items by CSS selector, fields by sub-selector text content (trimmed), list fields collect all matches in order. `embedded-json`: locate declared marker string, base64-decode the following script content, parse JSON, select items by JSON path **with filter support** (required: filter syntax, e.g. `$.queries[?state.data.docs].docs[]`). Field coercion to declared types; key-field integrity failures fail the run naming the item.

- *Resolved dependency (spike done):* JSONPath library = **JsonCons.JsonPath 1.1.0** (Apache-2.0, built on System.Text.Json). The originally named candidates do not exist: Newtonsoft ships no JsonPath API and there is no `JsonEagle` package on NuGet. Spike against the live site's real payload: filter anchor `$.queries[?(@.state.data.docs)]` selects exactly the search query; relative evaluation works by re-rooting (`$.docs`, `$.price.amount`, `$.flags[0]` all resolve); missing paths return an empty result list (no throw) and invalid paths throw at parse time, which lets config validation reject bad paths before any network access. Note: this library speaks Goessner/JMESPath-style filters (`[?(@.…)]`), not RFC 9535 function syntax — the live-listings job config's anchor uses that form.
- *Alternative considered:* hand-rolled path walker — rejected; the filter step is site-specific and a tested library beats bespoke parsing.

### D7: Pagination strategies as named capabilities
`next-link`: CSS selector → href resolved absolute against page URL (existing convention). `page-param`: read current/last from declared locations in page data, rebuild URL with param = current+1 preserving other query parameters. Both return null at the last page; runner loop unchanged around them.

### D8: Runner semantics preserved, items become documents
The existing run loop is reused as-is: 50-page cap, visited-set cycle detection (not a failure), stop-at-first-failed-page with persist-of-collected, non-zero exit on any failed page. Items change from typed records to ordered documents (`Dictionary<string, object?>`-shaped); snapshot serializers adapt — JSON serialization is direct; the Markdown serializer gains a document-aware rendering path (it currently reflects over record properties).

### D9: Host CLI and scheduling unchanged in kind
System.CommandLine 2.0.12 model-based API (`RootCommand`, `SetAction(parseResult => …)`, `parseResult.GetValue<T>`), Cronos for `--schedule` with immediate catch-up run, SIGTERM via apphost binary (Dockerfile builds the apphost; `dotnet <dll>` would let the muxer eat SIGTERM). CLI shape: `<config.yaml> [output] [--schedule '<cron>']`; start URL comes from config.

### D10: One image, Postgres service in compose
Single Dockerfile for the host apphost. Compose: one Postgres service (own volume) + one service per config file — same image, different command (`configs/x.yaml`) and env (`TZ`, connection string). The `/data` named volume is removed; dev = `podman compose up -d db` plus running jobs against the local instance.

## Risks / Trade-offs

- [JSONPath library quality/expressiveness falls short] → Phase-3 spike validates filter syntax against the live site's real payload before any engine code depends on it; fallback is a second candidate library, both are small and swappable behind the `embedded-json` capability.
- [No test suite — regressions caught only by live runs] → Verification gate includes an idempotency check (second run writes zero rows/versions) and history-on-real-change-only check against quotes.toscrape.com and the live listings site; both sites' expected item counts are known from current behavior.
- [jsonb numeric representation drift (int vs numeric storage)] → App-side coercion produces stable JSON types per declared field type, and equality is semantic, so representation differences cannot cause spurious changes.
- [History growth: one full document per change, unbounded] → Acceptable at scraper scale; retention/compaction explicitly deferred (Non-Goal) rather than designed speculatively.
- [`MAX(version)+1` assumes single writer per table] → True by construction (one job owns its tables); documented here so a future multi-writer feature forces an explicit re-examination instead of silently breaking.
- [Breaking cutover: old projects deleted, old DBs abandoned] → Rollback is `git revert` to the pre-change commit; no data migration exists or is needed because the data was declared disposable.

## Migration Plan

1. Build engine library + host against the new storage (fresh local Postgres via compose).
2. Author the two example job configs (`examples/quotes.yaml` + the live-listings config); live-run both; verify item counts match current behavior, second run writes nothing, history rows appear only on real change.
3. Cutover: delete both job projects, typed entities, `ScraperDbContext`, `Migrations/`, EF packages; update solution file, Dockerfile, and compose (Postgres service in, `/data` volume out).
4. Deploy via `podman compose up -d --build`; fresh Postgres database is the new source of truth.

Rollback: revert to the pre-change commit — old projects and SQLite path are intact there; no data recovery step exists or is required (disposable by decision).

## Open Questions

- **JSONPath library choice** (Newtonsoft.Json.Path vs JsonEagle) — answered by the phase-3 spike; does not change specs, approach, or task breakdown.
- **`dump <config>` command** (write JSONL from the database as the LLM-analysis handoff interface) — proposed during exploration, not yet in the `job-execution` spec's CLI contract; confirm before adding to v1 scope rather than silently expanding it.
