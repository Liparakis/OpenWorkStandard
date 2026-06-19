# CLI Reference

## Direction

The current CLI is a local reference client for OWS assessment provenance.

Near-term architecture:

- the client observes
- the server notarizes when configured
- the final package proves
- the professor decides

Today, the CLI is still mostly local-only. Remote verifier integration has started at the domain-model and minimal API scaffold level, but the CLI does not call a live verifier over the network yet.

## `ows init`

- Purpose: initialize local OWS state for a project.
- Usage: `ows init`
- Options: none yet
- Current behavior: creates `.ows/`, `.ows/config.json`, and `.ows/timeline.jsonl`
- Example output: `OWS initialized at <path>`
- Status: implemented MVP command

## `ows watch`

- Purpose: capture local project evidence.
- Usage: `ows watch`
- Options: none yet
- Current behavior: performs one scan of the current project and appends `FileCreated` events for existing files
- Example output: `OWS watch completed one scan.`
- Status: partial MVP command, not a persistent watcher

## `ows package`

- Purpose: create a submission package.
- Usage: `ows package`
- Options: none yet
- Current behavior:
  - creates a real `.owspkg` archive
  - writes `manifest.json`, `timeline.jsonl`, and `version_graph.json`
  - includes project files under `artifacts/`
  - stores hashes for timeline, version graph, and packaged artifacts
- Example output: `OWS package created at <path>`
- Status: implemented MVP command

## `ows verify`

- Purpose: verify a submission package.
- Usage: `ows verify`
- Options: none yet
- Current behavior:
  - validates package structure
  - validates manifest, timeline, and version graph JSON
  - validates timeline, version graph, and artifact hashes
  - rejects undeclared packaged artifacts
  - returns a trust grade
- Current trust behavior:
  - locally valid packages are currently graded `Unverified`
  - structural or hash failures are graded `Invalid`
  - remote receipts are not integrated yet
- Example output: `OWS verify succeeded.`
- Status: implemented MVP command with trust grading foundation

## `ows report`

- Purpose: generate an integrity report from verification output.
- Usage: `ows report`
- Options: none yet
- Current behavior:
  - runs verification first
  - writes a text report to `<project>.report.txt`
  - includes verification status, trust grade, summary, and errors
- Example output: `OWS report created at <path>`
- Status: implemented MVP command

## Planned CLI Extensions

These are directionally planned, not implemented yet:

- `ows session start`
- `ows checkpoint`
- `ows verify --server <url>`
- `ows package --include-receipts`
- `ows report --format text|json`

The current rule is simple: do not break the existing local workflow while adding the remote trust boundary foundation.
