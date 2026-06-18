# OWS Architecture

## System layers

- `Ows.Core`: domain types, hashing, manifests, version graph, verification primitives, constants.
- `Ows.Agent`: local tracking and file-watching shell for future event capture.
- `Ows.Packaging`: package assembly boundary for `.owspkg`.
- `Ows.Verification`: package validation and integrity review boundary.
- `Ows.Reporting`: report rendering boundary for JSON, text, and HTML output.
- `Ows.Cli`: operator-facing entry point for the reference implementation.
- `Ows.Desktop`: future Avalonia surface, intentionally unimplemented today.

## Component responsibilities

- The CLI orchestrates commands and user-facing output.
- The agent will own local evidence capture and `.ows/` maintenance.
- Core stays platform-agnostic and should not depend on UI or OS installers.
- Packaging turns local evidence into a submission artifact.
- Verification reads packages and returns structured outcomes.
- Reporting translates verification results into readable output formats.

## Data flow

Tracked project activity eventually flows through this path:

1. `ows init` establishes local OWS metadata.
2. `ows watch` will collect project-scoped events and state transitions.
3. The local `.ows/` store will persist timelines, snapshots, deltas, and metadata.
4. `ows package` will assemble the evidence into `.owspkg`.
5. `ows verify` will validate integrity and reconstructability.
6. `ows report` will render neutral reports for human review.

## Local `.ows/` storage

The local evidence folder lives at `.ows/` inside the tracked project. The initial design reserves space for configuration, events, snapshots, deltas, graph data, reports, and future signing material. This folder is local implementation state, not a substitute for a final submission package.

## `.owspkg` packaging

`.owspkg` is the portable submission artifact. The first implementation assumes a ZIP-based container because it is inspectable, ubiquitous, and easy to verify across platforms.

## Cross-platform considerations

- Core logic must stay independent of Windows and macOS APIs.
- File watching should treat native watcher signals as hints and add a polling fallback later.
- Package verification must not depend on the operating system that created the package.
- Paths should be normalized consistently when stored in manifests, events, and graphs.

## Privacy model

OWS is limited to project-scoped provenance. It should capture work history relevant to the assignment and avoid personal telemetry. Privacy is a system boundary, not an afterthought.

## Security model

The first architecture assumes hash-based tamper evidence, explicit verification outcomes, and future digital signature support. Security claims should remain conservative until hashing, package reconstruction, and signing are fully implemented and tested.
