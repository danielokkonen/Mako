# job-execution Specification

## Purpose
Defines the host executable's observable contract: command-line interface, run-loop behavior including pagination limits and failure handling, snapshot output, exit codes, and cron scheduling with graceful shutdown.

## Requirements

### Requirement: Command-line interface
The host SHALL accept a job configuration file path (required), an optional snapshot output path, and an optional `--schedule '<cron>'` flag. The start URL is taken from the configuration; it is not a command-line argument.

#### Scenario: Single run invocation
- **WHEN** the host is invoked with a valid configuration path only
- **THEN** exactly one scrape-and-persist run occurs and the process exits with the run's exit code

#### Scenario: Missing configuration file
- **WHEN** the given configuration path does not exist or the file is not valid YAML for this format
- **THEN** the host fails with a clear error and non-zero exit before any network access

### Requirement: Run loop and pagination limits
A run SHALL follow next-page URLs until none remains, capped at 50 pages per run. A previously visited URL MUST terminate the loop as a detected pagination cycle without being treated as a fetch failure.

#### Scenario: Cap reached
- **WHEN** pagination would require more than 50 pages
- **THEN** the run stops after the 50th page and persists what was collected

#### Scenario: Loop detected
- **WHEN** a next-page URL has already been fetched in this run
- **THEN** the loop terminates, is reported as such, and does not count as a failed page

### Requirement: Failure handling and exit codes
A page failure — robots.txt denial, HTTP error after retries, or an open circuit breaker for the host — SHALL stop pagination at that page. Items collected before the failure MUST still be persisted. The process exits non-zero if any page failed; zero otherwise.

#### Scenario: Robots denial mid-run
- **WHEN** a page is disallowed by robots.txt after earlier pages succeeded
- **THEN** the run stops, persists the items collected so far, and exits non-zero

#### Scenario: Clean run
- **WHEN** every fetched page succeeds
- **THEN** all items are persisted and the process exits zero

### Requirement: Snapshot output
When an optional snapshot path is given, the run SHALL additionally write a file containing all items from the run. Paths ending in `.md` or `.markdown` produce Markdown; any other extension produces JSON. The file MUST be written atomically (temporary file plus rename).

#### Scenario: Markdown snapshot
- **WHEN** the snapshot path ends in `.md`
- **THEN** a Markdown rendering of all collected items is written to that path

#### Scenario: JSON snapshot
- **WHEN** the snapshot path has any other extension
- **THEN** a JSON document containing all collected items is written to that path

### Requirement: Scheduled execution
With `--schedule '<cron>'`, the host SHALL run once immediately on start (catch-up), then at each scheduled time until terminated; without the flag it runs once and exits. A failed scheduled run MUST be logged and retried at the next interval rather than terminating the process. An invalid cron expression MUST fail before any run occurs.

#### Scenario: Catch-up run
- **WHEN** the host starts with a valid schedule
- **THEN** one full run executes immediately, before waiting for the first scheduled time

#### Scenario: Failed interval retried
- **WHEN** a scheduled run fails (non-zero result)
- **THEN** the process continues and the next scheduled time triggers another attempt

#### Scenario: Invalid cron expression
- **WHEN** the schedule value is not a valid cron expression
- **THEN** the host exits non-zero with an error before performing any scrape

### Requirement: Graceful shutdown
While in scheduled mode, the host SHALL stop cleanly on SIGTERM (and Ctrl+C), completing or abandoning the current run without hanging.

#### Scenario: SIGTERM during idle wait
- **WHEN** SIGTERM arrives while waiting for the next scheduled time
- **THEN** the process exits promptly and cleanly
