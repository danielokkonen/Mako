## Purpose

Defines the YAML job configuration format: how a scraper job declares its target site, extraction rules, natural key, and storage table, and how invalid configurations are rejected before any network activity.

## ADDED Requirements

### Requirement: Job definition file
A scraper job SHALL be fully defined by a single YAML file. A valid configuration MUST declare at minimum: a unique job name, an absolute http(s) start URL, a target table name, the item selector for the declared source kind, one or more field mappings, and the list of natural-key fields.

#### Scenario: Minimal valid configuration
- **WHEN** a YAML file declares name, url, source, items, at least one field, key, and table
- **THEN** the engine accepts it as a runnable job without any code being written or compiled

#### Scenario: Missing mandatory element
- **WHEN** a configuration omits the start URL or the natural-key declaration
- **THEN** the engine rejects the job with an error naming the missing element, before any HTTP request is made

### Requirement: Field mappings
Each field mapping SHALL declare its name, its source (a CSS selector for `dom` sources or a JSON path for `embedded-json` sources), and a type from the supported set (`text`, `integer`, `long`, `real`, `date`). A mapping MAY declare a transform from the built-in registry, a default value used when extraction yields nothing, and mark itself as a list field whose value is an ordered array.

#### Scenario: List field declaration
- **WHEN** a field mapping is marked as a list field
- **THEN** its extracted value in the stored document is a JSON array containing one entry per match, in document order

#### Scenario: Default applied on empty extraction
- **WHEN** a non-key field's selector or path matches nothing and the mapping declares a default
- **THEN** the stored document contains the declared default for that field

### Requirement: Pagination strategy declaration
A configuration MAY declare exactly one pagination strategy. `next-link` MUST be accompanied by a CSS selector; `page-param` MUST name the URL query parameter and the locations of the current-page and last-page values within the page data. A configuration with no pagination strategy is a single-page job.

#### Scenario: Unknown strategy rejected
- **WHEN** a configuration names a pagination strategy that is not registered
- **THEN** the engine rejects the job with an error naming the unknown strategy, before any HTTP request is made

### Requirement: Request options
A configuration MAY declare additional request headers and a cookie value. Cookie values MAY reference environment variables via `${VAR}` interpolation; the referenced variable MUST be set at run time or the run fails before any request.

#### Scenario: Missing environment variable
- **WHEN** a configured cookie references an environment variable that is not set
- **THEN** the run fails immediately with an error naming the variable, and no HTTP request is made

### Requirement: Validation precedes execution
The engine SHALL validate the complete configuration (structure, declared types, strategy names, selector syntax) before performing any network access or database write.

#### Scenario: Invalid type name
- **WHEN** a field mapping declares a type outside the supported set
- **THEN** the run fails with an error identifying the field and the invalid type value
