## Context

See proposal.md for motivation. Current state: there is no test project; the only end-to-end check is a live smoke run against real sites, which exercises the network/DB path but not the pure decision logic. `Scraper.Core` already isolates that logic into pure or near-pure units — `FieldCoercion`, `ConfigLoader.Load`, the two pagination strategies, and the two item sources — with no hidden state in the coercion/pagination paths. The "hands" (`ScrapingClient`'s static `HttpClient`, `DocumentStore`'s direct `NpgsqlConnection`) are deliberately out of scope for this change (Tier 2/3).

Constraints that shape the approach:
- Target framework is **net10.0** only (this machine has just the .NET 10 SDK/runtime).
- AngleSharp is **1.x**: parse with `new HtmlParser().ParseDocument(html)`; `GetAttribute(name)` returns `string?`.
- No EF Core; storage is raw ADO.NET over Npgsql — but we are not touching storage here.

## Goals / Non-Goals

**Goals:**
- A fast, offline unit-test net over the pure decision logic already specified in the existing `data-extraction` and `job-configuration` specs, so refactors that silently corrupt data (wrong type, dropped key, broken URL, mis-mapped field) fail locally.
- Plain asserts; serial execution (the NUnit default).
- Zero production code changes.

**Non-Goals:**
- No integration tests against a real Postgres (Tier 3) or live HTTP (Tier 2).
- No refactoring of `ScrapingClient`/`DocumentStore` to add injection seams — deferred to later changes.
- Not exhaustive coverage; the target is the behavioral contracts that catch regressions, mapped to existing spec requirements/scenarios.

## Decisions

1. **Framework: NUnit** (chosen). Rationale: NUnit runs serially by default, which sidesteps parallel-race flakiness for the two tests that read process-global environment state — no collection/fixture plumbing required up front. Alternatives considered: xUnit (parallel-by-default → would force isolating env-var tests into a serial collection) and MSTest; both viable but NUnit's serial default best fits our one footgun, per user preference.

2. **Plain asserts, not golden/snapshot files** (chosen). Rationale: each test names the exact behavior it pins and stays self-contained; less drift than committing fixture output. Alternative considered: golden JSON via our own `JsonSerializer` (matches the repo's snapshot culture) — deferred; can be layered on later without conflict.

3. **Test project layout:** new `tests/Scraper.Core.Tests` referencing `Scraper.Core`, added to `Scraper.slnx` under a `tests/` folder. One test class per area: `FieldCoercionTests`, `ConfigLoaderTests`, `PageParamPaginationTests`, `NextLinkPaginationTests`, `DomItemSourceTests`, `EmbeddedJsonItemSourceTests`, plus small helper tests (`JsonPaths`, `CapabilityRegistry`, `StripCurlyQuotesTransform`, `DocumentStore.IsValidTableName`).

4. **Fixture strategy — use the real types, no fakes.**
   - Pagination/extraction need a `ScrapedPage`. It is a public record with a public constructor `(IReadOnlyList<ScrapedItem> Items, IHtmlDocument Document, Func<string, object?>? ReadPayloadValue)`, so tests construct it directly.
   - **`NextLinkPagination`:** build a real document via `new HtmlParser().ParseDocument(html)` containing the selector-matched element with an `href`; wrap in `ScrapedPage`. This exercises the actual AngleSharp query path (no fake DOM).
   - **`PageParamPagination`:** it only reads current/last through `ReadPayloadValue`, so supply a synthetic delegate returning the desired values — no real payload required.
   - **Item sources:** call `ExtractAsync(html, pageUrl, job)` with hand-built HTML / base64 JSON payloads and a constructed `SiteJob`; assert on the returned `ScrapedPage.Items`.

5. **`ConfigLoader.Load` tests use temp files** (the API takes a path). Write YAML to a unique temp file per test; clean up in teardown. For the cookie `${VAR}` case, save/restore the environment variable in `[SetUp]`/`[TearDown]`.

6. **Env-var discipline:** any test reading `Environment.GetEnvironmentVariable` (the cookie `${VAR}` path and `PostgresStorage.ConnectionString`) saves the original in `[SetUp]` and restores it in `[TearDown]`. Execution stays serial, so there is no cross-test race; this also future-proofs us if parallelization is ever enabled.

## Risks / Trade-offs

- **AngleSharp behavior drift** — tests assert observable outputs of parsing (selector matches, trimmed text). Mitigation: reference the same AngleSharp version `Scraper.Core` uses and assert on extracted values/URLs, not internal DOM shape.
- **Temp-file / env-var leakage if teardown is skipped** — NUnit runs `[TearDown]` even when a test fails; still prefer unique temp paths so a leaked file can't collide with another test.
- **"Important parts" is judgment-based** — we are not targeting 100% coverage, so an edge could be missed. Mitigation: map each test to a specific existing spec requirement/scenario so gaps are visible against the specs rather than invisible.
- **Static shared `HtmlParser` in sources/ConfigLoader** — stateless per `ParseDocument` call; safe under serial tests. No action needed, noted for completeness.

## Migration Plan

N/A — additive only (new project + one solution entry). Rollback = delete the test project and its `Scraper.slnx` reference; no production impact.

## Open Questions

- Whether to add a convenience `dotnet test` target or CI hook later. Out of scope today (no CI per AGENTS.md); deferrable without changing specs, approach, or task breakdown.
- Whether helper tests live in their own small classes or fold into the nearest area file — cosmetic; answerable during implementation.
