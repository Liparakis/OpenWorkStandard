# Privacy Boundaries

## What OWS collects

OWS is intended to collect only project-scoped provenance, such as:

- file creation, modification, and deletion events inside the tracked project
- project open and close events
- build, execution, and test events related to the tracked work
- hashes, deltas, snapshots, manifests, and package metadata
- tool names when they are relevant to the tracked project workflow

## What OWS must never collect

OWS must never collect:

- raw keystrokes
- passwords or secrets
- browser history
- browser page content
- private messages
- webcam data
- microphone data
- unrelated personal files outside the tracked project
- automated guilt scores presented as facts

## Design stance

Privacy is a product boundary. If a proposed feature requires broader device surveillance, it does not belong in OWS without a fundamental change to the project mission and documentation.

## Current Storage and Retention Reality

OWS is local-first today, but it still creates retained data.

Current local retained data may include:

- `.ows/timeline.jsonl`
- `.ows/config.json`
- `.ows/session.json`
- `.ows/receipts.json`
- `.owspkg` output files
- local verifier logs under `artifacts/local-verifier/`

Current remote retained data may include:

- verifier sessions
- durable checkpoints
- durable receipts
- package object metadata, package hashes, package sizes, and verification status
- verifier metadata needed to reconstruct session head state
- verifier request logs containing method, path, status code, duration, and possible session IDs in paths

## Current Retention Behavior

Today, retention is mostly manual and operator-controlled.

- local project evidence stays on disk until the user deletes it
- local verifier logs stay on disk until the operator deletes them
- PostgreSQL-backed verifier data stays in the configured database until the operator applies retention policy outside the app
- verifier request logs do not intentionally include request bodies, file contents, raw headers, API keys, or receipt signing keys

OWS does not yet enforce automatic retention expiry, institutional retention windows, or legal hold behavior.

## Current Privacy Limits

What this means in practice:

- OWS minimizes scope better than surveillance tools, but it is not zero-retention
- self-hosters and institutions must decide how long verifier data should remain
- the current MVP should not promise policy-grade retention controls that do not exist yet

## Future Requirements

Before institutional rollout, OWS should add or document:

- explicit retention windows for sessions, receipts, and reports
- operator guidance for log retention and deletion
- data export and deletion expectations
- separation between project evidence retention and infrastructure log retention
