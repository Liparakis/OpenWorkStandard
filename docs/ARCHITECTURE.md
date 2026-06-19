# OWS Architecture

## Positioning

OWS is evolving from a purely local evidence packaging tool into an open, self-hostable assessment notarization protocol with an optional managed verifier service.

Core principle:

- the client observes
- the server notarizes
- the final package proves
- the professor decides

OWS is not an AI detector, surveillance product, or automated misconduct judge. It is assessment provenance infrastructure designed to support manual review with tamper-evident evidence.

## Trust Boundary

The student machine is not trusted.

That leads to the core security split:

- the local client is an evidence collector
- the remote verifier is the trust boundary
- the final package is the portable proof artifact

OWS should not claim to prevent all cheating. It should claim that a clean verified package requires the local timeline to match the remote verifier's receipt chain.

## Current Repository State

The current repository implements a thin but real local/reference slice:

- `ows init` creates local `.ows` state
- `ows watch` performs a one-shot scan
- `ows package` creates real `.owspkg` archives
- `ows verify` validates package integrity, event chains, and optional receipt chains
- `ows report` writes a basic text report
- receipt/session/checkpoint/receipt-chain domain models exist in `Ows.Core`

What does not exist yet:

- persistent host-owned watcher implementations
- production storage adapters
- background worker pipeline
- deployed infrastructure manifests

## System Layers

- `Ows.Core`: domain types, hashing, event chains, notarization models, packaging, verification, and reporting primitives
- `Ows.Core.Agent`: local tracking shell and future watcher boundary
- `Ows.Core.Notarization`: session, checkpoint, receipt, and receipt-chain foundation
- `Ows.Core.Packaging`: package assembly for `.owspkg`
- `Ows.Core.Verification`: package validation, trust grading, and integrity findings
- `Ows.Cli`: reference client entry point
- `Ows.Desktop`: future host surface, still placeholder-only today
- `Ows.Verifier.Server`: minimal ASP.NET Core verifier API scaffold backed by a local JSON receipt snapshot
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

### Human reviewer

- interpret verification output
- decide whether degraded/unverified intervals are acceptable
- make assessment decisions outside the protocol

## Data Flow

Current implemented flow:

1. `ows init` creates local state.
2. `ows watch` appends chained local events.
3. `ows session start` can create a local or remote-backed receipt session, depending on transport wiring.
4. `ows session checkpoint` can append a receipt to the current session, depending on transport wiring.
5. `ows package` writes a real `.owspkg` archive.
6. optional `receipts.json` is included when present locally.
7. `ows verify` validates event-chain integrity, artifact integrity, and optional receipt-chain integrity, with optional live verifier cross-checking.
8. `ows report` renders a basic text integrity report.

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

OWS should separate durable truth from caches and blobs.

Durable source of truth:

- PostgreSQL for institutions, users, roles, course structure, assessments, sessions, checkpoints, receipts, verification reports, package metadata, and audit events

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

The default design target is privacy-preserving assessment provenance, not invasive monitoring.

## Current Gaps

The main missing architecture pieces are:

- persistent host-owned watcher implementations
- verifier API and worker processes
- PostgreSQL/Redis/object-storage adapters
- lease/session lifecycle policy
- richer degraded/unverified trust policies
- deployment automation

That is the right order: architecture and transport discipline first, infrastructure rollout second.
