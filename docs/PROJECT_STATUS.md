# OWS Project Status

Last updated: 2026-06-19

## Summary

Open Work Standard now has a real end-to-end reference flow, not just placeholders.

What works today:

- local project initialization
- one-shot timeline capture
- local or remote-backed session start and checkpoint issuance
- real `.owspkg` package creation
- package verification with trust grading
- optional live verifier cross-checking during verification
- text report generation
- a minimal ASP.NET Core verifier server with selectable JSON or PostgreSQL storage

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
- `Ows.Desktop` is still placeholder-only
- `Ows.Verifier.Server` is intentionally small and self-hostable, not production-ready

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
- scans the project tree once
- skips files under `.ows/`
- appends chained file events to `.ows/timeline.jsonl`

What does not work yet:

- no long-running watcher
- no background lifecycle
- no IDE host integration
- no lease or heartbeat model

Status:

- useful proof of capture
- not an always-on watcher

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
- includes status, trust grade, summary, and errors

Status:

- working
- basic output only

## Verifier Server

`src/Ows.Verifier.Server` currently provides:

- `POST /sessions`
- `POST /sessions/{id}/checkpoints`
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
- package assembly
- package verification and trust grading

## Testing Status

Current automated coverage includes:

- initialization
- one-shot watch behavior
- session start and checkpoint flows
- local and remote receipt transport paths
- package creation
- package verification success and failure cases
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

Net result:

- OWS now has a real remote receipt path
- package verification can meaningfully consult a verifier
- the package format now carries enough session context to resolve remote anchors

## Current Gaps

The main missing pieces are:

- persistent always-on watcher lifecycle
- platform-specific hosts for VS Code, Rider, and desktop
- deployed validation of the PostgreSQL verifier backend
- multi-instance verifier deployment model
- server-side package submission and verification
- richer degraded-policy handling
- stronger human review reports
- desktop UI beyond placeholder state

## Reality Check

What is solid:

- packaging and verification logic
- trust-model direction
- command/test discipline
- small, coherent project structure

What is still weak:

- capture fidelity
- long-running tracking
- operational trust guarantees
- production verifier storage and hosting

The weakest assumption to avoid: thinking the current verifier server is already a real institutional trust boundary. It is not. It is a good foundation, not the finished boundary.

## Recommended Next Steps

Best next steps, in order:

1. validate the PostgreSQL verifier path in an actual local deployment flow
2. keep the verifier API narrow and boring while hardening storage semantics
3. add a minimal package-submission or package-anchor path only after storage is durable
4. defer IDE plugins and background hosts until the watcher lifecycle is clearer

## Bottom Line

OWS currently has a credible MVP for:

- local evidence initialization
- tamper-evident local event capture in one-shot form
- local or remote receipt issuance
- packaging evidence into `.owspkg`
- verifying package integrity and receipt alignment
- consulting a live verifier during verification

It does not yet have a credible always-on capture model or a production-grade remote trust boundary, but it now has the right minimal seams to grow into both.
