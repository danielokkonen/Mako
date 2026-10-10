## 1. Setup

- [x] 1.1 Create `tests/Scraper.Core.Tests` targeting net10.0, referencing `src/Scraper.Core`.
- [x] 1.2 Add NUnit + Microsoft.NET.Test.Sdk package references; confirm `dotnet test` discovers tests in the project.
- [x] 1.3 Add the project to `Scraper.slnx` under a `tests/` folder; confirm solution build includes it.

## 2. FieldCoercion (spec: data-extraction → "Field type coercion and key integrity")

- [x] 2.1 `Coerce` per type: text passthrough (string + invariant render of non-string); integer from string/long/int/double-integral; out-of-range int → null; long; real from string/int/double; date from ISO (offset-less assumed UTC) and Unix epoch ms; uncoercible → null.
- [x] 2.2 `BuildItem` scalar: present → coerced; absent with declared default → default; absent without default → null.
- [x] 2.3 `BuildItem` key integrity: missing or uncoercible natural-key field throws `PageExtractionException` naming the item position and field.
- [x] 2.4 `BuildItem` list fields: matches → coerced element list; zero/absent → null (absent path).
- [x] 2.5 Natural key format: values joined by the unit separator, date formatted as "O"; assert the exact key string for a representative item.

## 3. ConfigLoader.Load (spec: job-configuration)

- [x] 3.1 Valid minimal config round-trips to a `SiteJob` with trimmed name/table and parsed absolute URL.
- [x] 3.2 Fail-fast aggregation: multiple invalid elements produce one `ConfigurationException` listing all errors together.
- [x] 3.3 Required-element rejections: missing/blank name, url (incl. non-http(s) or relative), table (invalid identifier) — each named in the error.
- [x] 3.4 Source validation: unregistered kind names the registered set; dom items invalid CSS rejected; embedded-json missing marker/anchor and invalid JSON path rejected.
- [x] 3.5 Field mapping rules: empty fields list, duplicate names, unsupported type (names supported set), unknown transform — each rejected with an identifying message.
- [x] 3.6 Key cross-rules: key referencing an undeclared field rejected; a list field included in the key rejected.
- [x] 3.7 Pagination declaration: next-link requires valid selector; page-param requires embedded-json source + param/current/last with valid JSON paths; unknown strategy named.
- [x] 3.8 Cookie `${VAR}`: unset env var → error naming it (no request); set env var → interpolated into `SiteJob.Cookie`. Save/restore the env var in `[SetUp]`/[TearDown].

## 4. Pagination (spec: data-extraction → next-link + page-param)

- [x] 4.1 PageParamPagination middle page: current < last → URL with param = current+1, other query params preserved in order.
- [x] 4.2 PageParamPagination replace-in-place vs append: existing param replaced (no duplicate); absent param appended.
- [x] 4.3 PageParamPagination stop conditions: current >= last → null; missing/uncoercible page numbers → null.
- [x] 4.4 NextLinkPagination resolution: absolute href as-is; relative href resolved against the page URL (build a real AngleSharp document).
- [x] 4.5 NextLinkPagination last-page: no match, empty/whitespace href, unusable href (`UriFormatException`) → null.

## 5. Extraction (spec: data-extraction → DOM + embedded JSON)

- [x] 5.1 DomItemSource items+fields: N matched elements → N docs; scalar field = trimmed sub-element text; list field collects all matches in document order.
- [x] 5.2 DomItemSource transform applied to extracted values; no-match on the items selector throws naming the selector.
- [x] 5.3 EmbeddedJsonItemSource happy path: marker found, base64 JSON decoded, anchor selects exactly one node, items path selects exactly one array → items with scalar fields at relative paths.
- [x] 5.4 EmbeddedJsonItemSource list field (array) vs absent; `ToPrimitive` mapping (string / integral-long / double / bool / null-object-array).
- [x] 5.5 EmbeddedJsonItemSource failure modes: marker absent, no closing `</script>`, invalid base64, invalid JSON, anchor matched 0/>1 nodes, items path selected ≠1 array — each throws `PageExtractionException` with an identifying message.

## 6. Helpers

- [x] 6.1 `JsonPaths.Root` (leading `$` preserved vs re-rooted) + Parse of an invalid path → `ConfigurationException` naming the field.
- [x] 6.2 `CapabilityRegistry` case-insensitive lookup returns the registered capability; `Registered*` strings are sorted.
- [x] 6.3 `StripCurlyQuotesTransform` strips leading/trailing curly quotes and passes non-string values through unchanged.
- [x] 6.4 `DocumentStore.IsValidTableName` accepts valid identifiers and rejects invalid ones (leading uppercase, illegal chars, empty, >63).

## 7. Verification

- [x] 7.1 `dotnet build` on the solution succeeds with the new project included.
- [x] 7.2 `dotnet test` runs all tests green; each test maps to a specific requirement/scenario in `data-extraction` or `job-configuration` (no orphan assertions).
