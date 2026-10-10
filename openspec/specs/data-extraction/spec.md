# data-extraction Specification

## Purpose
Defines how a page (HTML or embedded JSON payload) is turned into typed item documents, and how the next-page URL is derived for each supported pagination strategy.

## Requirements

### Requirement: DOM source extraction
For `dom` sources, the engine SHALL produce one document per element matching the items selector. Scalar field values are the text content of the matched sub-element (trimmed); list fields collect the text content of every match in document order.

#### Scenario: Items extracted from page
- **WHEN** a page contains three elements matching the items selector and each declares fields `text` and `author`
- **THEN** extraction yields three documents, each carrying the trimmed text content of its sub-elements for those fields

#### Scenario: No matches on a page
- **WHEN** no element matches the items selector
- **THEN** the page yields zero items and the run reports the page as failed with an error indicating the selector matched nothing

### Requirement: Embedded JSON source extraction
For `embedded-json` sources, the engine SHALL locate the declared marker in the HTML, base64-decode the script content following it, parse the resulting JSON, and select items by the declared JSON path. The JSON path MUST support member access, array iteration, and filter expressions.

#### Scenario: Payload located and parsed
- **WHEN** the page contains the declared marker followed by a valid base64-encoded JSON payload
- **THEN** extraction proceeds over the decoded payload's item array

#### Scenario: Marker absent
- **WHEN** the page does not contain the declared marker, or the script content is not decodable/parseable JSON
- **THEN** the run reports the page as failed with an error identifying the missing or malformed payload

### Requirement: Field type coercion and key integrity
Field values SHALL be coerced to their declared types. Natural-key fields MUST be present and coercible for every extracted item; a violation fails the run with an error identifying the offending item. Non-key fields that are absent or uncoercible take their declared default, or null when no default is declared.

#### Scenario: Key field missing
- **WHEN** an extracted item lacks a value for a natural-key field
- **THEN** the run fails with an error naming the key field and the item position

#### Scenario: Non-key field absent without default
- **WHEN** an optional non-key field's path matches nothing and no default is declared
- **THEN** the stored document contains null for that field and the run succeeds

### Requirement: Next-link pagination
Under the `next-link` strategy, the next-page URL SHALL be the `href` of the element matching the declared selector, resolved to an absolute URL against the page URL. When no element matches, or the matched element has no usable href, there is no next page.

#### Scenario: Next link present
- **WHEN** the selector matches an element whose href resolves against the page URL
- **THEN** the run fetches that absolute URL as the next page

#### Scenario: Last page detected
- **WHEN** the selector matches nothing (or yields no usable href)
- **THEN** pagination stops after the current page

### Requirement: Page-parameter pagination
Under the `page-param` strategy, the engine SHALL read the current and last page numbers from their declared locations in the page data. When current is less than last, the next-page URL is the page URL with the declared query parameter set to current + 1, preserving all other query parameters; otherwise there is no next page.

#### Scenario: Middle page
- **WHEN** the payload reports current = 2 and last = 5 for parameter `page`
- **THEN** the next page URL is the same URL with `page=3`, other query parameters unchanged

#### Scenario: Final page
- **WHEN** the payload reports current equal to last
- **THEN** pagination stops after the current page
