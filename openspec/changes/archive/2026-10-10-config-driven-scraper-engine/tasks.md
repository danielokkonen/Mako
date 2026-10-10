# Tasks: Config-Driven Scraper Engine

## 1. Storage Layer (Postgres-only)

- [x] 1.1 Add Npgsql to core project; remove `Microsoft.EntityFrameworkCore.*` packages from all projects and delete EF-specific DI registration
- [x] 1.2 Implement idempotent schema creation for a job: data table `(key TEXT UNIQUE, data JSONB, changed_at timestamptz)` + history table `(key, version INT, data JSONB, changed_at timestamptz)`, PK `(key, version)`; verify repeat runs leave existing schema/data untouched
- [x] 1.3 Implement the upsert: `INSERT … ON CONFLICT (key) DO UPDATE … WHERE excluded.data IS DISTINCT FROM <table>.data RETURNING key`; confirm identical documents produce zero writes (no timestamp bump, no version)
- [x] 1.4 Implement history appends: first sight of a key inserts v1 with the data row; changed keys append `MAX(version)+1` in the same transaction as the update; verify run-level atomicity (failure mid-persist commits nothing)

## 2. Configuration Model + Loader

- [x] 2.1 Add YamlDotNet; define the `SiteJob` model covering: name, url, table, key fields, source kind + selector/marker, field mappings (source, type, transform, default, list), pagination strategy, request headers/cookie
- [x] 2.2 Implement YAML loading with full validation before any network/DB access: missing mandatory elements, unknown types, unknown strategy names, invalid selectors — each error names the offending element; `${VAR}` env interpolation for cookie values with fail-fast on unset variables

## 3. Extraction Capabilities

- [x] 3.1 Spike JSONPath library (Newtonsoft.Json.Path vs JsonEagle) against Blocket's real payload shape (`$.queries[?state.data.docs].docs[]`); record the choice
- [x] 3.2 Implement `dom` source: AngleSharp parse, items by CSS selector, scalar fields = trimmed sub-element text, list fields collect all matches in document order; zero-match page fails with a named-selector error
- [x] 3.3 Implement `embedded-json` source: marker lookup, base64 decode, JSON parse, item selection via the chosen JSONPath library; missing/malformed payload fails the page with an identifying error
- [x] 3.4 Implement field coercion to declared types (`text`, `integer`, `long`, `real`, `date`) with defaults/null for absent non-key fields; missing or uncoercible key field fails the run naming the item position

## 4. Pagination Strategies

- [x] 4.1 Implement `next-link`: selector match → href resolved absolute against page URL; no match / unusable href = last page
- [x] 4.2 Implement `page-param`: read current/last from declared locations, rebuild URL with param = current+1 preserving other query parameters; current ≥ last = stop

## 5. Runner Adaptation

- [x] 5.1 Re-point the existing run loop at document items (keep 50-page cap, visited-set cycle detection, stop-at-first-failed-page + persist-collected, non-zero exit on failure)
- [x] 5.2 Adapt snapshot serializers to documents: JSON direct; Markdown serializer gains a document-aware rendering path replacing record-property reflection

## 6. Host Executable

- [x] 6.1 Create `src/Scraper.Host` exe project (System.CommandLine 2.0.12 model-based API): `<config.yaml> [output] [--schedule '<cron>']`; missing/invalid config file fails before network access
- [x] 6.2 Wire DI composition root: register storage, built-in source/pagination/transform capabilities by name; unknown capability names surface as config errors
- [x] 6.3 Wire Cronos `--schedule` (immediate catch-up run, failed interval logged + retried, invalid cron fails before any run) and SIGTERM/Ctrl+C graceful shutdown; add project to `Scraper.slnx`

## 7. Site Migration

- [x] 7.1 Author `configs/quotes.yaml` reproducing current Quotes behavior (dom source, next-link pagination, key = text+author, list field tags)
- [x] 7.2 Author `configs/blocket.yaml` reproducing current Blocket behavior (embedded-json source with marker/base64, page-param pagination, key = id)
- [x] 7.3 Delete `src/Scraper.QuotesToscrape` and `src/Scraper.Blocket`, typed entities (`QuoteRow`, `QuoteTag`, `BlocketListing`), `ScraperDbContext`, `Migrations/`; remove both projects from the solution; confirm clean build

## 8. Docker / Compose

- [x] 8.1 Point the Dockerfile at the host apphost binary (apphost required for SIGTERM)
- [x] 8.2 Update `compose.yaml`: add Postgres service with its own volume; one service per config file sharing the image (per-service command + env: connection string, `TZ`); remove the `/data` named volume

## 9. Verification (live runs)

- [x] 9.1 Start local Postgres via compose; run quotes job against https://quotes.toscrape.com/ — item count matches current behavior, schema created, history v1 rows present
- [x] 9.2 Re-run quotes job immediately — zero new rows and zero new versions (idempotency gate)
- [x] 9.3 Run blocket job live — listings persist with correct types; second run writes nothing; a real site-side change appends exactly one version per changed key
- [x] 9.4 Verify failure paths: robots denial / bad URL exits non-zero after persisting collected items; `--schedule` mode runs catch-up then idles, SIGTERM exits cleanly
