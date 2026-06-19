# OWS Architecture

## Positioning

OWS is moving from a purely local evidence packaging tool toward an open, self-hostable assessment notarization protocol with an optional managed verifier service.

The intended model is:

- the client observes
- the server notarizes
- the final package proves
- the professor decides

OWS is not an AI detector, surveillance product, or automated misconduct judge. It is assessment provenance infrastructure designed to support manual review with tamper-evident evidence.

## Trust Boundary

The local client is not the final security authority because the student controls the machine.

That leads to one hard rule:

- local capture is evidence collection
- remote verification is the trust boundary

In the current codebase, OWS has only started moving toward that boundary. The package verifier can now distinguish integrity status from trust status, but remote receipts are still foundation-only domain models.

## System Layers

- `Ows.Core`: domain types, hashing, manifests, events, notarization models, verification primitives, and constants
- `Ows.Core.Agent`: local tracking shell and future watcher boundary
- `Ows.Core.Packaging`: package assembly for `.owspkg`
- `Ows.Core.Verification`: package validation, trust grading, and integrity findings
- `Ows.Core.Reporting`: report rendering for verification output
- `Ows.Cli`: reference client entry point
- `Ows.Desktop`: future host surface, still placeholder-only today

## Responsibility Split

### Local client

- initialize local `.ows/` state
- observe project file activity
- produce local timeline events
- hash local evidence
- build final `.owspkg` packages
- include remote receipts when available

### Remote verifier

- receive checkpoints
- assign server timestamps
- issue signed or otherwise authoritative receipts
- maintain receipt chains per assessment session
- compare final packages against known receipts

The remote verifier is not implemented yet in this repository. Only the core receipt/session model foundation exists today.

## Current Data Flow

Current implemented flow:

1. `ows init` creates local state.
2. `ows watch` performs a one-shot scan and appends `FileCreated` events.
3. `ows package` writes a real `.owspkg` archive.
4. `ows verify` validates local package integrity and assigns a trust grade.
5. `ows report` renders a basic text integrity report.

Target flow:

1. local client collects append-only tamper-evident events
2. client submits checkpoint hashes to a remote verifier
3. remote verifier returns timestamped receipts
4. package includes local evidence plus receipts
5. verification compares local package state against receipt chains

## Verification States

OWS should explicitly model trust states instead of only success/failure:

- `Verified`: local timeline and remote receipts align cleanly
- `Degraded`: evidence is mostly usable but includes explainable concerns
- `Unverified`: local package is consistent, but trust anchors are missing or incomplete
- `Invalid`: package structure, hash integrity, or receipt integrity is broken

Current implementation status:

- local-only successful packages are graded `Unverified`
- structural or hash failures are graded `Invalid`
- `Verified` and `Degraded` are reserved for later receipt-aware verification

## Local Storage

The `.ows/` folder remains local implementation state inside the project. It is useful for capture, but it is not sufficient by itself to provide strong trust guarantees against the machine owner.

That is why local-only history should be treated as:

- useful
- testable
- sometimes trustworthy
- never the sole authority for high-assurance verification

## Packaging

`.owspkg` remains the portable submission artifact.

Current package contents:

- `manifest.json`
- `timeline.jsonl`
- `version_graph.json`
- `artifacts/...`

Near-term package direction:

- keep local evidence inspectable
- add receipt material when remote notarization is configured
- keep privacy bias toward hashes, metadata, timestamps, and manifests rather than raw remote file storage

## Privacy Model

OWS should prefer:

- project-scoped provenance
- minimal metadata
- self-hostable verification
- receipt/checkpoint exchange over raw file upload

The default design target is privacy-preserving assessment provenance, not invasive monitoring.

## Security Model

OWS should not claim impossible local security.

What it should claim instead:

- local evidence can be made tamper-evident
- remote notarization can strengthen trust
- a clean locally valid package is not the same as a fully verified package
- integrity gaps should be reported as unverified intervals, not as definitive cheating claims

## Current Gaps

The main missing architecture pieces are:

- persistent host-owned watcher implementations
- append-only local event chain verification
- remote verifier server endpoints
- receipt-aware package verification
- richer trust policies and reporting

That is the next milestone: remote trust boundary foundation, not “perfect local security.”
