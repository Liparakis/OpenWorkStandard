# OWS Data Model

OWS models project-scoped evidence and package integrity. The filesystem and `.ows/` directory are the primary local observation boundary.

## Local project state

- `.ows/config.json`: OWS version, absolute project root, initialization time, and watcher settings.
- `.ows/timeline.jsonl`: append-only chained `OwsEvent` records.
- `.ows/version_graph.json`: the current text-first graph placeholder.
- `.ows/observed_snapshot.json`: local recovery state used to explain observation gaps.

## Package state

An `.owspkg` contains `manifest.json`, `timeline.jsonl`, `version_graph.json`, optional `signature.json`, and opaque `artifacts/...` entries. Manifest hashes and the canonical package-root hash make the logical contents independently checkable.

## Trust and privacy

Verification reports package integrity, timeline continuity, signature state, and neutral observation findings. OWS does not resolve institutions, courses, rosters, students, assessments, or grades. It never collects raw keystrokes, passwords, browser data, webcam/microphone data, or unrelated personal files.
