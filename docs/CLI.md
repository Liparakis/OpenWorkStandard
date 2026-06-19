# CLI Reference

## Direction

The current CLI is a local reference client for OWS assessment provenance.

Near-term architecture:

- the client observes
- the server notarizes when configured
- the final package proves
- the professor decides

Today, the CLI is still mostly local-only. It can now start sessions and submit checkpoints against a configured verifier API, while keeping `timeline.jsonl`, `session.json`, and `receipts.json` as the local package inputs.

## `ows session start`

- Purpose: start an assessment session for local or remote receipt issuance.
- Usage: `ows session start [--server <url>]`
- Options:
  - `--server <url>`: route receipt issuance through a verifier API instead of the local-only prototype flow
- Current behavior:
  - without `--server`, creates local `.ows/session.json` and `.ows/receipts.json`
  - with `--server`, starts a remote session and persists the verifier URL locally
- Example output: `OWS session started: <session-id>`
- Status: implemented MVP command

## `ows session checkpoint`

- Purpose: issue the next receipt for the current timeline head.
- Usage: `ows session checkpoint`
- Options: none yet
- Current behavior:
  - derives the checkpoint from `.ows/timeline.jsonl`
  - issues the next receipt locally or remotely based on `.ows/session.json`
  - updates `.ows/receipts.json`
- Example output: `OWS checkpoint recorded: <receipt-hash>`
- Status: implemented MVP command

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
  - includes `session.json` when local session metadata exists
  - includes `receipts.json` when local receipts exist
  - includes project files under `artifacts/`
  - stores hashes for timeline, version graph, and packaged artifacts
- Example output: `OWS package created at <path>`
- Status: implemented MVP command

## `ows verify`

- Purpose: verify a submission package.
- Usage: `ows verify [--server <url>]`
- Options:
  - `--server <url>`: cross-check packaged session and receipts against a live verifier API
- Current behavior:
  - validates package structure
  - validates manifest, timeline, and version graph JSON
  - validates packaged session metadata when present
  - validates timeline, version graph, and artifact hashes
  - rejects undeclared packaged artifacts
  - optionally cross-checks the packaged session against a live verifier receipt chain
  - uses the verifier session head when `session.json` exists but `receipts.json` is absent
  - returns a trust grade
- Current trust behavior:
  - locally valid packages are currently graded `Unverified`
  - structural or hash failures are graded `Invalid`
  - valid packaged receipts can upgrade trust to `Verified`
  - `verify --server` can resolve the remote session from packaged `session.json` even when `receipts.json` is absent
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

- `ows package --include-receipts`
- `ows report --format text|json`

The current rule is simple: do not break the existing local workflow while adding the remote trust boundary foundation.
