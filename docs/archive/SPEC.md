> Archived: this document may be outdated. See `../core/OVERVIEW.md`, `../core/ARCHITECTURE.md`, and `../core/PACKAGE_FORMAT.md` for current guidance.

# OWS Specification

## Purpose

Open Work Standard defines a way to capture the evolution of academic work, package the resulting provenance locally, and verify it later without relying on surveillance or AI detection.

## Scope

The initial scope is a local-first reference implementation for coursework provenance. It focuses on project initialization, event capture, local storage, package assembly, and later verification. The first implementation is CLI-led and cross-platform.

## Design principles

- Process over output: work evolution matters more than a final snapshot alone.
- Verification over detection: the system supports review of provenance instead of guessing whether a tool was used.
- Privacy first: only project-scoped provenance data belongs in OWS.
- Local first: evidence is stored locally before export.
- Human review: review signals assist instructors but never make decisions for them.
- Open standards: package structure and event semantics remain inspectable.

## Package concept

An `.owspkg` file is a portable archive containing the manifest, timeline, version graph, and related artifacts needed for later inspection. The package is intended to travel with a submission while remaining independent from any single IDE or institution-specific system.

## Event model

OWS normalizes observed activity into a common event schema. Initial event categories are:

- `FileCreated`
- `FileModified`
- `FileDeleted`
- `ProjectOpened`
- `ProjectClosed`
- `BuildStarted`
- `BuildSucceeded`
- `BuildFailed`
- `ProgramExecuted`
- `TestExecuted`
- `LargeInsert`
- `PackageCreated`

These events describe project-scoped provenance. They do not imply misconduct and they do not include invasive telemetry.

## Watcher lifecycle

`ows watch` runs in two sequential phases:

1. **Initial scan** — all files currently present in the tracked project are recorded as `FileCreated` events, chained together in the provenance timeline. This establishes the baseline state.

2. **Continuous monitoring** — after the initial scan the watcher subscribes to native OS file-system signals (`FileSystemWatcher` on Windows and macOS) and, in parallel, runs a periodic polling loop as a fallback. When a file change is detected, the event is held until a configurable quiet window (default 500 ms) has elapsed with no further changes to the same path. This debounce step collapses burst activity (e.g. auto-save followed immediately by a formatter rewrite) into a single provenance event.

3. **Graceful stop** — when the process receives SIGINT (Ctrl+C) the watcher flushes any pending debounced events to the timeline and stops cleanly. The final result status is `Stopped`.

The polling fallback is always available via `ows watch --poll` and is the recommended mode for network-mounted file systems or restricted CI environments where native OS signals are unreliable.

## Work Version Graph concept

The Work Version Graph models work evolution as a directed acyclic graph. Nodes represent versions or states of work. Edges represent transformations between them. A simple project may look linear, but a DAG leaves room for branching, merges, imports, and future multi-tool workflows.

## Verification model

Verification checks package integrity, manifest validity, timeline readability, graph consistency, and hash continuity. The result should distinguish between:

- clean verification success
- verification failure with explicit errors
- neutral review signals that suggest additional human review

Verification is evidence-oriented, not accusation-oriented.

## Non-goals

OWS does not implement or endorse:

- AI detection
- proctoring
- browser lockdown
- webcam or microphone capture
- keylogging
- cloud-first storage requirements
- automatic misconduct judgment
> [!WARNING]
> Archived document. This may contain outdated design notes.
> Current guidance lives under docs/core/, docs/workflows/, docs/operations/, and docs/integrations/.
