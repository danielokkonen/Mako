# data-persistence Specification

## Purpose
Defines document-first persistence in the shared Postgres database: per-site tables keyed by natural key, jsonb documents as the canonical record, append-only version history, and never-delete semantics.

## Requirements

### Requirement: Idempotent schema creation
At job start, the engine SHALL ensure the job's data table and its history table exist in the shared Postgres database, creating them when absent. Starting a job any number of times against an existing database MUST be safe and MUST NOT alter or drop existing tables, rows, or versions.

#### Scenario: First run creates schema
- **WHEN** a configured job runs for the first time against a fresh database
- **THEN** its data table (unique natural-key column, jsonb document column, changed-at timestamp) and history table (key, version, jsonb document, changed-at timestamp) are created

#### Scenario: Repeat run leaves schema untouched
- **WHEN** the same job runs again against a database that already contains its tables with data
- **THEN** no existing table structure, row, or history version is modified by schema creation

### Requirement: Natural-key upsert with semantic change detection
Items SHALL be stored as JSON documents under their natural key (the declared key fields combined). A document identical to the stored one — ignoring key order and whitespace — MUST produce no write of any kind: no updated timestamp, no new history version.

#### Scenario: New key inserted
- **WHEN** an item's natural key is not present in the data table
- **THEN** the document is inserted into the data table and stored as version 1 in the history table

#### Scenario: Unchanged re-run writes nothing
- **WHEN** a run yields documents identical to every currently stored document for that job
- **THEN** no row or history version is written and changed-at timestamps are untouched

#### Scenario: Changed document updates both tables
- **WHEN** an item's document differs from the stored one in any field value
- **THEN** the data table holds the new document and a new history version containing it is appended, atomically with the update

### Requirement: Append-only version history
The history table SHALL contain exactly one row per distinct version of each key, numbered sequentially from 1. Version 1 corresponds to first sight of the key; every subsequent change appends the next version carrying the full new document. The engine MUST NOT delete or overwrite any data-table row or history version under any circumstance.

#### Scenario: Version sequence
- **WHEN** a key's document changes twice across successive runs
- **THEN** its history contains exactly three versions, in order, each holding the full document as it was at that change

#### Scenario: Point-in-time recovery
- **WHEN** a timestamp T is given for which the key had already been seen but not yet changed to its latest state
- **THEN** the document in effect at T is recoverable as the highest-numbered version with changed-at ≤ T

### Requirement: Run-level atomic persistence
All items collected during one run SHALL be persisted as a single unit: either every item's upsert and history append commits, or none do.

#### Scenario: Failure mid-persist
- **WHEN** persisting a run's items fails partway through
- **THEN** no partial state is committed for that run
