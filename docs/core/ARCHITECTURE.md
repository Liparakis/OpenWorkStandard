# OWS Architecture

## Positioning

OWS is a local-first proof-of-work protocol and toolchain with an optional remote verifier service.

Core principle:

- the local Agent observes
- the package records hashes and optional signatures
- an optional server provides remote receipts
- a human reviewer interprets evidence

OWS is not an AI detector, surveillance product, or automated misconduct judge. It is academic work provenance infrastructure designed to support manual review with tamper-evident evidence.

## Trust Boundary

The student machine is not trusted.

That leads to the core security split:

- the local client is an evidence collector
- the remote verifier is an optional stronger trust anchor
- the final package is the portable proof artifact

OWS should not claim to prevent all cheating. It can report package integrity and, when available, alignment with a remote receipt chain; local verification remains useful without that anchor.

## Current Repository State

The current repository implements a thin but real local/reference slice:

- `ows init` creates local `.ows` state
- `ows init` registers a project with the local Agent
- the Windows `Ows.Setup.exe` SCM service or `ows agent run` manages long-running observation
- `ows package` creates real `.owspkg` archives
- `ows verify` validates package integrity, event chains, and optional receipt chains
- `ows inspect` provides a local reviewer summary
- `ows report` writes a basic text report
- receipt/session/checkpoint/receipt-chain domain models exist in `Ows.Core`

Remaining scope is tracked in `docs/development/PROJECT_STATUS.md` and
`docs/development/ROADMAP_CHECKLIST.md`; Linux/macOS service adapters and
production verifier deployment remain deferred.

## System Layers

- `Ows.Core`: domain types, hashing, event chains, notarization models, packaging, verification, and reporting primitives
- `Ows.Core.Agent`: local registry, watcher, recovery, and package-coordination boundary
- `Ows.Core.Notarization`: session, checkpoint, receipt, and receipt-chain foundation
- `Ows.Core.Packaging`: package assembly for `.owspkg`
- `Ows.Core.Verification`: package validation, trust grading, and integrity findings
- `Ows.Cli`: reference client entry point
- `Ows.Desktop`: future host surface, still placeholder-only today
- `Ows.Verifier.Server`: minimal ASP.NET Core verifier API scaffold backed by a selectable storage boundary
- current storage seam: `IVerifierStorage` with JSON dev backend and PostgreSQL backend available
- future `Ows.Verifier.Worker`: background processing boundary

## Responsibility Split

### Local client

- initialize local `.ows/` state
- observe project file activity
- produce a tamper-evident local timeline
- derive checkpoint inputs from the local timeline head
- build final `.owspkg` packages
- include remote receipts when available

### Remote verifier

- receive checkpoints
- assign authoritative server timestamps
- issue chained receipts
- persist durable verified history
- verify final packages against known receipt history
- expose both full receipt-chain reads and a cheap current-head read

### Human reviewer

- interpret verification output
- decide whether degraded/unverified intervals are acceptable
- make assessment decisions outside the protocol

## Data Flow

Current implemented flow:

1. `ows init` creates local state and registers the project with the Agent.
2. The Agent appends chained local events while the project changes.
3. `ows package` flushes the Agent when available and writes a real `.owspkg` archive.
4. Optional `session.json` and `receipts.json` are included when present locally.
5. `ows verify <package>` validates event-chain integrity, packaged session integrity, artifact integrity, and optional receipt-chain integrity, with optional live verifier cross-checking.
6. `ows inspect <package>` and `ows report <package>` provide local reviewer views.

Target flow:

1. local client collects append-only tamper-evident events
2. client derives checkpoint inputs from the local timeline head
3. client sends checkpoints to the remote verifier
4. remote verifier returns timestamped chained receipts
5. package includes local evidence plus receipts
6. verification compares local package state against receipt chains

## Verification States

OWS models trust states instead of only success/failure:

- `Verified`: local timeline and remote receipts align with no meaningful gaps
- `Degraded`: evidence is mostly usable but includes explainable concerns
- `Unverified`: local package is consistent, but trust anchors are missing or incomplete
- `Invalid`: package structure, hash integrity, event-chain integrity, or receipt integrity is broken

Current implementation status:

- local-only successful packages are graded `Unverified`
- successful packages with a valid matching packaged receipt chain are graded `Verified`
- structural, hash, event-chain, or receipt-chain failures are graded `Invalid`
- `Degraded` is reserved for later lease/gap policy work

## Storage Roles

OWS should separate verifier durable truth from caches and blobs. OWS does not own institutions, courses, rosters, grading, or other management records.

Durable source of truth:

- PostgreSQL for verifier sessions, checkpoints, receipts, package metadata, verification results, opaque external context identifiers, and audit events

Ephemeral shared state:

- Redis/Valkey for session leases, heartbeats, rate limits, idempotency keys, distributed locks, live dashboard pub/sub, and temporary coordination

Blob/object storage:

- S3-compatible storage for `.owspkg` package blobs, uploaded archives, large reports, export bundles, and optional encrypted starter files

Optional durable internal event transport:

- NATS JetStream later, only when a clean worker/event boundary exists

Redis must not be the source of truth. Large package blobs should not live in PostgreSQL.

## Horizontal Scaling

The verifier API must be stateless with respect to in-memory session ownership.

Scaling assumptions:

- any API instance should be able to receive the next checkpoint for any active session
- durable checkpoint and receipt history must live in PostgreSQL
- Redis may cache live session/lease state but cannot be authoritative
- API instances must be replaceable without losing verified history
- worker processes must be restartable without invalidating durable state

## Transport Direction

Do not start with custom TCP or DTLS.

Roadmap:

1. MVP: HTTPS REST over TLS
2. Streaming: gRPC over HTTP/2 TLS
3. Opportunistic upgrade: HTTP/3 over QUIC where supported
4. Future/experimental: direct QUIC transport only if justified later

Reasoning:

- REST/TLS is easiest to deploy and debug
- gRPC is a natural next step for streaming checkpoint flows
- HTTP/3/QUIC is a good optimization direction
- campus networks and proxies may block or degrade UDP/HTTP/3
- custom transports create security and operational surface area too early

Fallback support matters more than protocol novelty.

## Future Seams

When networked transport is added, keep the client protocol behind a narrow abstraction such as:

- `StartSessionAsync()`
- `SendCheckpointAsync()`
- `RefreshLeaseAsync()`
- `UploadPackageAsync()`
- `GetReceiptsAsync()`

That seam should allow:

- HTTPS transport first
- gRPC transport later
- HTTP/3-backed transport later

Do not add raw QUIC or custom networking until the default HTTPS path is mature.

## Privacy Model

OWS should prefer:

- project-scoped provenance
- hashes and metadata over raw file upload by default
- self-hostable verification
- minimal retained data
- manual review rather than automated accusations

The default design target is privacy-preserving academic work provenance, not invasive monitoring.

## Current Gaps

The main missing architecture pieces are:

- Linux/macOS installable Agent service adapters
- verifier API and worker processes
- PostgreSQL/Redis/object-storage adapters
- lease/session lifecycle policy
- richer degraded/unverified trust policies
- deployment automation

That is the right order: architecture and transport discipline first, infrastructure rollout second.
