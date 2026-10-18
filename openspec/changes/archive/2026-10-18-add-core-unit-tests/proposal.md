## Why

The engine's core logic — field type coercion, config validation, pagination URL derivation, and HTML/payload extraction — currently has no automated regression net. The only end-to-end check is a live smoke run against real sites (see AGENTS.md), which exercises the network/DB path but does not pin down the pure decision logic. A refactor or new feature that silently corrupts data (wrong coerced type, dropped natural key, broken next-page URL, mis-mapped field) would still build green and only surface as bad rows in Postgres. We want a fast, offline unit-test net around exactly this logic so refactors are safe.

## What Changes

- Add an NUnit test project `tests/Scraper.Core.Tests` that references `Scraper.Core`, wired into `Scraper.slnx`.
- Unit tests (plain asserts; serial execution — the NUnit default) covering the Tier 1 behavioral contracts already specified in `data-extraction` and `job-configuration`:
  - **FieldCoercion** — per-type coercion, out-of-range → null, absent → declared default → null, natural-key integrity (missing/uncoercible key field throws), list fields (matches vs zero → absent), natural-key join + date formatting.
  - **ConfigLoader.Load** — valid config round-trips; fail-fast with *all* errors reported together; cross-field rules (key must reference a declared field, list field cannot be in the key, `page-param` requires an `embedded-json` source); CSS/JSON-path syntax checked at load time; cookie `${VAR}` unset → error / set → interpolated.
  - **Pagination** — `PageParamPagination` next-URL derivation (current+1, other params preserved in order, replace-in-place vs append, `current >= last` stops) and `NextLinkPagination` href resolution (absolute/relative, no-match / empty / unusable href → stop).
  - **Extraction** — `DomItemSource` and `EmbeddedJsonItemSource`: item/field mapping, list-vs-scalar, transforms applied, missing → default/null, and each failure mode (no items match; marker absent; malformed base64/JSON) reporting an identifying error.
  - **Small helpers** — `JsonPaths.Root`/Parse, `CapabilityRegistry` name lookup + sorted `Registered*`, `StripCurlyQuotesTransform`, `DocumentStore.IsValidTableName`.
- **No production code changes.** This is Tier 1 only (zero refactoring). The HTTP seam for retry/backoff/circuit-breaker and the runner loop (Tier 2) and Postgres integration tests (Tier 3) are explicitly out of scope and deferred to later changes.

## Capabilities

### New Capabilities
<!-- None: no new behavior is introduced; this change adds a test harness, not system capability. -->

### Modified Capabilities
<!-- None: adding unit tests does not alter any requirement or runtime behavior. The tested behavior is already specified in the existing `data-extraction` and `job-configuration` specs, which these tests verify rather than modify. This change therefore opts out of spec deltas via `skip_specs: true`. -->

## Impact

- **New:** `tests/Scraper.Core.Tests` project (NUnit + Microsoft.NET.Test.Sdk), added to `Scraper.slnx` under a `tests/` folder.
- **Unchanged:** everything under `src/` — no production code, config, or schema changes.
- **Test hygiene:** the two tests that read process-global state (`ConfigLoader` cookie `${VAR}` path and `PostgresStorage.ConnectionString`) save/restore the environment variable in `[SetUp]`/`[TearDown]`; execution stays serial (NUnit default) so there is no parallel-race flakiness.
