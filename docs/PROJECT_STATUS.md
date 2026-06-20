# OWS Project Status

Last updated: 2026-06-20

Checklist source of truth:

- `docs/ROADMAP_CHECKLIST.md` tracks current capability status, partial work, and explicitly deferred scope.

## Summary

Open Work Standard now has a real end-to-end reference flow, not just placeholders.

What works today:

- local project initialization
- persistent file-system watcher with native OS signals and polling fallback
- local or remote-backed session start and checkpoint issuance
- real `.owspkg` package creation
- package verification with trust grading
- optional live verifier cross-checking during verification
- text report generation
- a minimal ASP.NET Core verifier server with selectable JSON or PostgreSQL storage
- local PostgreSQL-backed verifier startup, smoke testing, and helper-script generation
- optional verifier receipt signing with a configured server key
- config-backed verifier API guard with operator and reviewer scopes
- structured verifier request logging with request ids, actor scope, and safe resource metadata
- persistent verifier audit events plus operator diagnostics endpoints
- durable `.owspkg` blob intake with worker-backed server-side verification
- unified OwsWatchSessionManager lifecycle foundation library for host integrations
- machine-readable CLI commands with global `--json` option and API key redaction
- minimal VS Code extension supporting full student watch/session/package lifecycle and secure key storage
- documented pilot demo path covering operator setup, student workflow, reviewer report access, diagnostics, audit, and negative-path checks

The codebase is still MVP-grade, but it is no longer only a local packaging toy. It now has a thin remote trust-boundary slice.

## Current Solution Shape

Source projects:

- `src/Ows.Core`
- `src/Ows.Cli`
- `src/Ows.Desktop`
- `src/Ows.Verifier.Server`

Test projects:

- `tests/Ows.Core.Tests`
- `tests/Ows.Cli.Tests`

Notes:

- `Ows.Core` remains the collapsed MVP domain project
- `Ows.Desktop` is still placeholder-only (design specs detailed in `docs/DESKTOP_UI.md`)
- `Ows.Verifier.Server` is intentionally small and self-hostable, not production-ready
- `src/ows-vscode` is the VS Code extension codebase

## Implemented Capabilities

### `ows init`

What works:

- creates `.ows/`
- creates `.ows/config.json`
- creates `.ows/timeline.jsonl`

Status:

- working
- minimal

### `ows watch`

What works:

- prepares the local tracking agent
- performs an initial one-shot scan of the project tree
- skips files under `.ows/`
- appends chained file events to `.ows/timeline.jsonl`
- continues watching for file-system changes via `FileSystemWatcher` (native OS signals)
- falls back to a periodic polling loop when `--poll` is passed or when the native watcher fails
- debounces burst saves (default 500 ms quiet window) so rapid auto-save + formatter activity
  produces a single `FileModified` event per file rather than dozens
- appends `FileCreated`, `FileModified`, and `FileDeleted` chained events during the watch loop
- runs until Ctrl+C or cancellation; prints a clean stop message
- `--poll` flag forces the polling fallback (useful on network drives and restricted CI environments)
- `--debounce <ms>` controls the quiet-time window

What does not work yet:
- no daemon-based background service (the CLI process must stay open, though VS Code extension spawns it as a background child process).

Status:
- working persistent watcher
- polling fallback available for cross-platform environments
- host-managed start/stop CLI hooks and manager abstractions integrated

### `ows session start`

What works:

- starts a local session by default
- starts a remote verifier session with `--server <url>`
- persists `.ows/session.json`
- persists an initial `.ows/receipts.json`

Status:

- working MVP command

### `ows session checkpoint`

What works:

- derives the checkpoint from the current local `timeline.jsonl` head
- issues the next local receipt or remote verifier receipt
- refreshes `.ows/receipts.json`
- remote checkpoint retries can use `Idempotency-Key`

Status:

- working MVP command

### `ows package`

What works:

- creates a real `.owspkg` archive
- writes `manifest.json`
- writes `timeline.jsonl`
- writes `version_graph.json`
- includes `session.json` when present
- includes `receipts.json` when present
- includes project files under `artifacts/`
- excludes `.ows/` files from packaged artifacts
- excludes the output package itself from packaged artifacts
- hashes timeline, version graph, session state, and packaged artifacts

Status:

- working MVP implementation

### `ows verify`

What works:

- validates package structure
- validates manifest JSON
- validates timeline JSONL and chained event hashes
- validates version graph JSON
- validates timeline, version graph, session-state, and artifact hashes
- validates packaged receipt chains when present
- can cross-check packaged receipt chains against a live verifier with `--server <url>`
- can resolve the remote session from packaged `session.json` even if `receipts.json` is absent
- can fall back to verifier session head comparison when only packaged session metadata is present

Trust behavior today:

- `Unverified`: package is locally consistent but remote trust anchors are missing or incomplete
- `Verified`: package and receipt evidence align cleanly
- `Invalid`: structural, hash, event-chain, or receipt-integrity checks fail
- `Degraded`: reserved for later policy work, not meaningfully used yet

Status:

- strongest part of the current codebase

### `ows report`

What works:

- runs verification first
- writes `<project>.report.txt`
- can write `<project>.report.json` with `ows report --format json`
- includes status, trust grade, summary, errors, findings, and review signals

Status:

- working
- richer than the initial stub, but still not a full professor review workflow

## Verifier Server

`src/Ows.Verifier.Server` currently provides:

- `POST /auth/api-keys`
- `GET /auth/api-keys`
- `POST /auth/api-keys/{id}/revoke`
- `POST /sessions`
- `POST /sessions/{id}/checkpoints`
- `POST /packages`
- `POST /packages/upload`
- `PUT /packages/{id}`
- `POST /packages/{id}/verify`
- `GET /packages/{id}`
- `GET /packages/{id}/verification`
- `GET /packages/{id}/report`
- `GET /sessions/{id}/packages`
- `GET /sessions/{id}/receipts`
- `GET /sessions/{id}/head`

Storage model today:

- `json`: local JSON snapshot file for development
- `postgres`: transactional PostgreSQL-backed storage through the verifier storage abstraction

PostgreSQL setup model today:

- app-owned ordered migrations
- migration tracking in `ows_verifier_schema_version`
- explicit `migrate` bootstrap mode for self-hosting
- normal PostgreSQL server startup also applies missing migrations
- persistent verifier API keys are stored as hash-only durable records
- persisted keys expose creation, listing, revocation, optional expiry, key prefix display, and last-used timestamps
- checkpoint requests are validated before storage append
- idempotent checkpoint retries are enforced in both JSON and PostgreSQL storage
- receipts include an HMAC server signature when `VerifierStorage:ReceiptSigningKey` is configured
- requests require `X-OWS-Verifier-Key` when verifier API keys are configured
- the current auth slice supports full-access `Operator` keys and institution-scoped read-only `InstructorReviewer` keys
- the legacy shared bootstrap key remains supported through `VerifierSecurity:ApiKey`
- request logs include `X-Request-Id`, method, path, status code, elapsed time, role, institution scope, and key prefix
- `GET /audit/events` exposes operator-only audit queries with simple filters for institution, session, package, event type, and time
- `GET /diagnostics/summary` exposes lightweight safe counters instead of a full monitoring stack
- `/ready` now reports safe dependency state for storage, education store reachability, package storage, signing configuration, and auth mode
- audit events cover API key lifecycle, auth failures, access denials, session creation, checkpoint/heartbeat acceptance, lease-gap detection, package submission, package verification, and report reads
- package uploads stream into local durable blob storage with server-side content-addressed object keys
- package submissions persist package SHA-256, package size, verification job id, and latest verification error
- package submission retries can use `Idempotency-Key`
- duplicate package object registrations reject metadata drift
- registered package submission metadata can be fetched by submission ID
- registered package submission metadata can also be listed by verifier session
- package registration captures the current verifier session head when `sessionId` is supplied
- package bytes are not stored in PostgreSQL
- package verification runs through a durable in-process job worker with restart recovery for stale running jobs
- package report reads are available through `GET /packages/{id}/report`

Local verifier dev flow today:

- `docker-compose.local.yml` starts PostgreSQL using `D:\Containers\OWS\postgres\data`
- `scripts/doctor-local-verifier.ps1` and `scripts/validate-local-verifier.ps1` / `.sh` perform read-only local environment diagnostics and preflight checks
- `scripts/run-local-verifier.ps1` runs PostgreSQL, migrations, and the verifier in the foreground
- `scripts/start-local-verifier.ps1`, `status-local-verifier.ps1`, `logs-local-verifier.ps1`, and `stop-local-verifier.ps1` provide background lifecycle helpers on Windows
- `scripts/test-local-verifier.ps1` performs a direct API smoke check
- `src/Ows.Verifier.Server/Dockerfile` builds the `ows-verifier:local` verifier image
- helper scripts now resolve the repo root correctly from both `scripts/` and `artifacts/generated-scripts/`
- helper status distinguishes `not_started`, `running`, `stale_pid`, `crashed`, `unreachable`, and `port_in_use`
- foreground and background verifier helpers auto-build the verifier server when the local build output is missing
- verifier status and smoke-test helpers send `X-OWS-Verifier-Key` from `OWS_VERIFIER_API_KEY` when present
- `dotnet build` emits platform-specific launcher copies to `artifacts/generated-scripts/`
- verifier logs now include per-request method, path, status, and duration
- local verifier smoke tests cover package upload, asynchronous verification completion, idempotent retry, lookup by ID and session, and report retrieval

This is enough for architectural validation and the first durable-backend pass. It is still not enough for institutional trust claims until the PostgreSQL path is exercised in a real deployed environment.

## Core Domains Present

`Ows.Core` currently includes:

- `Ows.Core.Agent`
- `Ows.Core.Events`
- `Ows.Core.Graph`
- `Ows.Core.Hashing`
- `Ows.Core.Init`
- `Ows.Core.Notarization`
- `Ows.Core.Packaging`
- `Ows.Core.Reporting`
- `Ows.Core.Verification`

Important implemented pieces:

- chained local event model
- receipt/session/checkpoint models
- receipt-chain verification
- provider-based verifier storage abstraction
- JSON-backed verifier storage
- PostgreSQL-backed verifier storage foundation
- PostgreSQL migration runner
- MVP verifier receipt signing
- durable API key lifecycle with hash-only storage
- config-backed verifier API guard with institution-scoped reviewer access
- verifier request validation
- idempotency-key enforcement
- package assembly
- package verification and trust grading
- package object metadata registration
- durable package blob intake
- worker-backed package verification jobs
- lightweight verifier observability foundation

## Testing Status

Current automated coverage includes:

- initialization
- one-shot watch behavior
- session start and checkpoint flows
- local and remote receipt transport paths
- checkpoint request validation
- idempotent retry behavior
- package creation
- package verification success and failure cases
- blob upload size and shape validation
- worker handling for missing package blobs
- receipt-chain verification
- live verifier cross-check behavior
- report generation
- serialization and hashing primitives

Latest verified commands:

```powershell
dotnet build OWS.sln -nologo
dotnet test OWS.sln -nologo
```

Both were passing at the time this document was updated.

## Recent Progress

Recent milestones:

- `eae9f45` `feat: persist verifier receipts locally`
- `7b65159` `feat: wire cli sessions to verifier api`
- `d1e895a` `feat: cross-check packaged receipts with verifier api`
- `434ad35` `feat: package session metadata for verifier lookup`
- `1f35295` `feat: add verifier session head endpoint`
- `ea4e8b6` `feat: add durable verifier storage foundation`
- `a789f1b` `feat: add verifier schema migration bootstrap`

Recent uncommitted/working-tree progress:

- PostgreSQL-backed verifier was validated in a live local Docker run
- session creation bug for `jsonb` metadata insert was fixed
- verifier host logging was simplified to console-only to avoid Windows Event Log permission failures
- checkpoint request validation was tightened before storage append
- local verifier helper scripts now cover run, start, status, logs, smoke-test, and stop
- build now emits platform-specific verifier helper scripts under `artifacts/generated-scripts/`
- a living roadmap checklist now tracks done, partial, and missing work in one place
- package uploads now store real `.owspkg` bytes on local verifier disk behind a blob-store abstraction
- package verification now runs through a durable job store and in-process worker instead of inline-only handling
- package verification results and reports persist across verifier restarts
- `InstitutionAdmin` and `StudentClient` RBAC roles are implemented with strict institution and ownership validation scoping
- Prometheus-compatible `/metrics` endpoint is exposed for scraping without key requirements
- operational runbooks, backup/restore order, recovery failure modes, and restore drills are fully documented
- `docs/PILOT_DEMO.md` now provides the main pilot walkthrough for professors and sysadmins
- `scripts/setup-pilot-fixture.ps1` creates a minimal institution/course/student/assessment fixture and delegated student/reviewer keys

Net result:

- OWS now has a real remote receipt path
- package verification can meaningfully consult a verifier
- the package format now carries enough session context to resolve remote anchors
- verifier-side package intake now survives restart and exposes operator/reviewer status endpoints
- OWS has a hardened production-readiness operational baseline with multi-institution scoping and monitoring support
- OWS has a documented end-to-end pilot validation path before adding Rider, desktop polish, or LMS integration

## Current Gaps

The main missing pieces are:

- platform-specific hosts for VS Code, Rider, and desktop
- multi-instance verifier deployment model
- desktop UI beyond placeholder state
- Single Sign-On (SSO) integration for dashboards (OIDC/SAML)
- external Grafana/Loki visualization integration

## Reality Check

What is solid:

- packaging and verification logic
- trust-model direction
- command/test discipline
- small, coherent project structure
- local verifier storage seam and local dev ergonomics
- comprehensive environment diagnostics, backup/restore drills, and operational runbooks
- multi-tenant scoping and key delegation security checks

What is still weak:

- capture fidelity
- long-running tracking
- operational trust guarantees beyond a single-node local blob store
- production verifier hosting and user identity dashboard integration

The weakest assumption to avoid: thinking durable local blobs plus PostgreSQL are already a finished institutional trust boundary. They are not. This is a better foundation, not the finished boundary.

## Recommended Next Steps

Best next steps, in order:

1. Run a full live pilot dry run against a fresh Compose stack and archive the evidence.
2. Fix any workflow seams found during that dry run.
3. Defer Rider, desktop polish, and LMS integration until the pilot path is boring.

## Bottom Line

OWS currently has a credible MVP for:

- local evidence initialization
- tamper-evident local event capture in one-shot form
- local or remote receipt issuance
- packaging evidence into `.owspkg`
- verifying package integrity and receipt alignment
- consulting a live verifier during verification
- submitting packages to a verifier for durable storage and asynchronous verification
- running a local PostgreSQL-backed verifier with generated platform-specific helper scripts

It does not yet have a credible always-on capture model or a production-grade remote trust boundary, but it now has the right minimal seams to grow into both.
