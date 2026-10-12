# Mako

A config-driven scraping engine built on .NET 10 and AngleSharp. A shared core library handles fetching, politeness (robots.txt), resilience, extraction, pagination, persistence, and scheduling; each target site is a single YAML file under `examples/` — no C# per site.

## Features

- **Config-driven jobs** — one YAML file per site: source kind (`dom` CSS selectors or `embedded-json` marker/base64/JSON path), typed field mappings, natural key, pagination strategy; the whole config is validated before any network or DB access
- **Scheduling** — jobs accept `--schedule '<cron>'` and loop until SIGTERM/Ctrl+C instead of exiting; with the flag they also run once immediately on start (catch-up), then fire on each scheduled time. Failed runs are logged and retried at the next interval
- **Containerized** — one Docker image with no configs baked in serves every job; each job's YAML is passed as raw text via `--yaml` at run time. `examples/compose.yaml` wires a Postgres service plus one service per example job
- **Polite & resilient HTTP client** — robots.txt checks, per-host circuit breaker, Polly retry with backoff/jitter, 1s delay between requests
- **Pagination** — `next-link` (CSS selector → absolute href) or `page-param` (current/last read from page data); the runner follows until exhausted (capped at 50 pages per run) and stops at the first failed page
- **Persistence** — all jobs share one Postgres database; each site gets a data table (one JSONB document per natural key) plus an append-only history table (one full-document version per change). Identical documents write nothing; rows and versions are never deleted
- **Optional snapshots** — any run can additionally write the collected items to a `.json` or `.md` file (format chosen by extension)

## Table of contents

- [Features](#features)
- [Layout](#layout)
- [Requirements](#requirements)
- [Build & run](#build--run)
  - [Database connection](#database-connection)
  - [Scheduling](#scheduling)
  - [Containers](#containers)
- [Adding a new site job](#adding-a-new-site-job)
  - [Top-level keys](#top-level-keys)
  - [Source](#source)
    - [`kind: dom`](#kind-dom)
    - [`kind: embedded-json`](#kind-embedded-json)
  - [Field mappings](#field-mappings)
    - [Types and coercion](#types-and-coercion)
    - [Natural key and stored documents](#natural-key-and-stored-documents)
  - [Pagination](#pagination)
  - [Worked examples](#worked-examples)

## Layout

| Path | What it is |
| --- | --- |
| `src/Mako.Core` | Engine library (`Mako.Core`): `ScrapingClient`, `RobotsTxtChecker`, config model + loader, extraction capabilities (`dom`, `embedded-json`), field coercion and transforms, pagination strategies, `DocumentStore` (raw ADO.NET over Npgsql — no EF Core), serializers, `CronScheduler`, `MakoRunner` |
| `src/Mako.Host` | Thin executable: CLI parsing, DI composition root registering the built-in capabilities by name, cron scheduling, SIGTERM handling |
| `examples/dom-source-example.yaml` | Job for https://quotes.toscrape.com (dom source, next-link pagination) |
| `examples/embedded-json-example.yaml` | Synthetic job showcasing the embedded-json source + page-param pagination (not wired into compose — no live site behind it) |
| `examples/compose.yaml` | Prod compose file: a Postgres service plus one service per example job |
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
dotnet run --project src/Mako.Host -- examples/dom-source-example.yaml [snapshot.json | snapshot.md] [--schedule '<cron>']
```

- There is no central dispatcher — you run a job by pointing the host at its config: either a YAML file path (the positional argument) or raw YAML text via `--yaml <raw yaml>`; exactly one of the two is required.
- Data is **always** persisted to the shared Postgres database (connection string resolution below). The optional 2nd argument additionally writes a snapshot file.
- Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker open). A failed fetch breaks the pagination chain but whatever was collected is still saved.

### Database connection

- Local runs default to this machine's Postgres (`Host=localhost;Port=5433;Username=user;Password=password;Database=mako`) — start it with `podman compose -f dev/compose.yaml up -d db`. No environment variable needed for local development.
- Override with the `MAKO_CONNECTION_STRING` env var. **Keyword form only** (`Host=db;Port=5432;Username=...;Password=...;Database=mako`) — Npgsql 10.x rejects URI format (`postgres://…`). The prod `examples/compose.yaml` sets it explicitly per service, pointing at its own Postgres container.

### Scheduling

```bash
dotnet run --project src/Mako.Host -- examples/dom-source-example.yaml --schedule '0 */6 * * *'   # every 6h on the hour
```

The cron expression is parsed by Cronos; an invalid expression exits with an error before anything runs. With `--schedule` the job also runs once immediately on start (catch-up for time the machine was off).

### Containers

One Dockerfile (`examples/Dockerfile`, build context = repo root) builds a single image that serves every job; `examples/compose.yaml` wires it up:

```bash
podman compose -f examples/compose.yaml up -d --build   # or: docker compose -f examples/compose.yaml up -d --build
```

- A `db` service (Postgres with its own volume) plus one service per example job — same image, no configs baked in; each service's command passes the job as raw YAML via `--yaml` (a block scalar, so no quoting issues) plus optional `--schedule '<cron>'`, and env.
- Cron time zone comes from the `TZ` env var (`MAKO_TZ`, defaults to UTC).

## Adding a new site job

A scraper job is a single YAML file. Adding a site never requires C#: write the YAML, run it — the data and history tables are created automatically at job start (`CREATE TABLE IF NOT EXISTS`), so there is nothing to migrate. The whole config is validated before any network or database access; when validation fails, every problem is reported together, each naming the offending element.

1. **Investigate the site.**
   - Find where items live: either repeated DOM elements (a CSS selector matches one element per item) or an embedded JSON payload — data shipped inside a `<script>` tag as base64-encoded JSON, common in React/SPA pages. Inspect the page source to locate it and note the exact script-tag prefix that identifies it.
   - For each field you want stored, find its location: a CSS sub-selector within an item element (dom), or a path relative to one item object (embedded-json).
   - Decide the **natural key**: the declared fields whose combined values uniquely identify an item across runs (e.g. an id, or `text` + `author`). Key fields must always be present and coercible — items missing them fail the page.
   - Identify pagination: a "next" link in the DOM (`next-link`), or a page number embedded in the payload plus a URL query parameter (`page-param`, embedded-json only). Omit pagination entirely for single-page jobs.
2. **Write one YAML file for the job** using the keys below (see [Worked examples](#worked-examples)).
3. **Run it** — run the built binary directly:

   ```bash
   ./Mako.Host <job.yaml>            # persist to Postgres
   ./Mako.Host <job.yaml> out.json   # additionally write a snapshot (.json or .md by extension)
   ```

   The start URL comes from the config file. Jobs exit non-zero if any page failed (robots denial, HTTP error after retries, circuit breaker open); stderr names the URL and reason. A failed fetch breaks the pagination chain but whatever was collected is still saved. Success prints `Done in …s: N items from M page(s)`.
4. **Optionally** run it through a container instead of (or alongside) the binary: one Docker image with no configs baked in serves every job, so add a service entry to `examples/compose.yaml` — same image, new command (the job's raw YAML via `--yaml`, plus optional `--schedule '<cron>'`) and env (`MAKO_CONNECTION_STRING`, `TZ`) — then start everything with `podman compose -f examples/compose.yaml up -d --build`.

### Top-level keys

| Key | Required | Meaning |
| --- | --- | --- |
| `name` | yes | Non-empty display name for the job. |
| `url` | yes | Absolute http(s) start URL of the first page. |
| `table` | yes | Target table: a lowercase letter followed by lowercase letters, digits, or underscores. The history table is `<table>_history`. One job owns its tables — don't share a table between configs (concurrent writers can collide on `(key, version)`). |
| `source` | yes | How items are extracted from a page ([below](#source)). |
| `fields` | yes | At least one field mapping ([below](#field-mappings)). |
| `key` | yes | One or more natural-key field names; each must be a declared field, and key fields cannot be list fields. |
| `pagination` | no | Omit for single-page jobs ([below](#pagination)). |
| `headers` | no | Map of header name → value, sent with every page request (e.g. an API key). |
| `cookie` | no | Value for the `Cookie` request header. `${VAR}` sequences are read from the environment at load time; a referenced variable that is unset fails validation. |

### Source

```yaml
source:
  kind: dom       # dom | embedded-json
  items: div.item # CSS selector of each item element (dom) / JSON path to the item array (embedded-json)
```

#### `kind: dom`

Items are the elements matched by `items`. Field `source` values are CSS sub-selectors evaluated within each item element. A page whose `items` selector matches nothing fails the page, naming the selector.

- **Scalar field**: the trimmed text content of the first element matching `source` inside the item; no match means absent.
- **List field** (`list: true`): every element matching `source`, in document order, each trimmed to its text content; zero matches means absent (stored as null).

#### `kind: embedded-json`

The page embeds its data as a base64 JSON payload inside a marked `<script>` tag. In addition to `items`:

```yaml
source:
  kind: embedded-json
  marker: <script type="application/json" data-react-query-state>   # exact script-tag prefix whose content holds the payload
  anchor: '$.queries[?(@.state.data.docs)].state.data'              # JSON path selecting the page-data object (exactly one node)
  items: .docs                                                      # JSON path to the item array, relative to the anchor (exactly one array)
```

- `marker` — the exact text of the opening script tag. The payload is everything between it and the next `</script>`, base64-decoded then parsed as JSON. A missing marker or malformed base64/JSON fails the page with an identifying error.
- `anchor` — a JSON path that must select **exactly one** node: the page-data object for the whole page.
- `items` — a JSON path, relative to the anchor, selecting **exactly one array**; each element of that array is one item.
- Field `source` values are JSON paths evaluated relative to each item. A scalar field takes the first selected value as a primitive (string/number/bool; null/object/array count as absent). A list field's path must select an array — its elements become the list; anything else means absent (stored as null).
- `pagination.current` / `pagination.last` are also anchor-relative JSON paths, resolved against this same payload.

JSON paths use JsonCons.JsonPath syntax. Note: recursive descent (`$..[]`) is rejected at parse time — to select multiple arrays use e.g. `$[*]` over an array-of-arrays instead.

### Field mappings

```yaml
fields:
  - name: title                     # unique within the job
    source: .title                  # CSS sub-selector (dom) / item-relative JSON path (embedded-json)
    type: text                      # text | integer | long | real | date
    transform: strip-curly-quotes   # optional, applied to the raw value before coercion
    default: ""                     # optional; used when extraction yields nothing or the value is uncoercible
    list: true                      # optional; field holds a list of values instead of one
```

| Key | Required | Meaning |
| --- | --- | --- |
| `name` | yes | Field name in the stored document. Must be unique within the job. |
| `source` | yes | Where to read it: CSS sub-selector (dom) or item-relative JSON path (embedded-json). Validated at load time. |
| `type` | yes | Declared type: `text`, `integer`, `long`, `real`, or `date`. The raw value is coerced to this type; uncoercible values become null (or the default, below). |
| `transform` | no | A built-in transform applied to the raw extracted value before coercion. Built in: `strip-curly-quotes` — replaces typographic curly quotes with straight ones. Unknown names fail validation. |
| `default` | no | Value used when extraction yields nothing, or the extracted value cannot be coerced to `type`. The YAML value (string/number/bool) is itself coerced to `type` at run time. Applies to scalar fields; list fields are stored as null when absent. |
| `list: true` | no | Field holds a list of values instead of one. Each element is coerced individually (uncoercible elements become null within the list). List fields cannot be key fields, and are stored as null when nothing matches. |

#### Types and coercion

| Type | Coerces from | Notes |
| --- | --- | --- |
| `text` | anything | Strings pass through; numbers/bools render invariant-culture. |
| `integer` | integral values | Strings parsed invariant-culture; integral doubles accepted; out of int range → null. |
| `long` | integral values | Same as above, full 64-bit range. |
| `real` | numeric values | Integral values widen to double; strings parsed invariant-culture. |
| `date` | ISO-8601 string or epoch ms | Offset-less timestamps are assumed UTC; any other integral number is Unix epoch milliseconds. Stored as a timestamp with offset. |

#### Natural key and stored documents

The item's natural key is the declared key fields' coerced values joined by a unit separator (U+001F); dates format as ISO-8601 (`O`). The key drives persistence: one data row per key plus an append-only history version on every change; identical documents write nothing.

### Pagination

```yaml
pagination:
  strategy: next-link   # next-link | page-param
  selector: li.next a   # next-link only
```

- **`next-link`** — `selector` (CSS) finds the "next" link on each page; its `href`, resolved absolute against the page URL, is the next page. No match or an unusable href means the current page is the last one. Works with both source kinds.
- **`page-param`** — for sites that paginate via a URL query parameter and expose the page numbers in their embedded payload (requires `kind: embedded-json`).
  - `param` — the URL query parameter name to set (e.g. `page`). The next URL is the current URL with that parameter set to *current + 1*, preserving every other query parameter and its order.
  - `current`, `last` — anchor-relative JSON paths to the current and last page numbers in the payload. Missing or uncoercible page numbers, or `current >= last`, stop pagination.

Runner behavior for both strategies: pages are followed until exhausted, capped at **50 pages per run**; a repeated URL (cycle) stops the loop without being a failure; the first failed page stops the run but everything collected up to that point is persisted.

### Worked examples

`dom-source-example.yaml` — dom source, next-link pagination:

```yaml
name: Quotes to Scrape
url: https://quotes.toscrape.com/
table: quotes
source:
  kind: dom
  items: div.quote
fields:
  - name: text
    source: .text
    type: text
    transform: strip-curly-quotes
  - name: author
    source: .author
    type: text
  - name: tags
    source: a.tag
    type: text
    list: true
key: [text, author]
pagination:
  strategy: next-link
  selector: li.next a
```

`embedded-json-example.yaml` — synthetic job showcasing the embedded-json source + page-param pagination (not wired into compose; no live site behind it):

```yaml
name: Example Listings
url: "https://example.com/search?category=1"
table: example_listings
source:
  kind: embedded-json
  items: .docs
  marker: <script type="application/json" data-page-data>
  anchor: '$.state.data'
fields:
  - name: id
    source: .id
    type: long
  - name: title
    source: .title
    type: text
    default: ""
  - name: price
    source: .price.amount
    type: integer
  - name: updated_at
    source: .updated_at
    type: date
    default: "1970-01-01T00:00:00Z"
key: [id]
pagination:
  strategy: page-param
  param: page
  current: .paging.current
  last: .paging.last
```
