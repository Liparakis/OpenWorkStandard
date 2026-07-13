# OWS Architecture

Open Work Standard is a local-first proof-of-work protocol/toolchain, not an LMS or hosted management system.

## Boundaries

- `Ows.Core` is platform-independent and owns observation, local state, packaging, and offline verification.
- `Ows.Cli` is the text-first interface: `init`, `agent`, `status`, `package`, `verify`, `inspect`, and `report`.
- `Ows.Setup` is the Windows-only installer/service boundary.
- The filesystem is the primary observation source. OWS watches only explicitly initialized projects.
- Binary files are opaque and hash-only.
- No server, account, password, keystroke capture, browser capture, or management database is required.

## Workflow

1. `ows init` creates `.ows/`, writes project configuration, and registers the explicit project root for the local Agent.
2. The Agent observes project-scoped filesystem activity and appends chained events to `.ows/timeline.jsonl`.
3. `ows package` creates a ZIP `.owspkg` containing the manifest, timeline, version graph, and artifact hashes. `--sign` adds an offline RSA signature.
4. `ows verify`, `ows inspect`, and `ows report` operate on the package without a network connection.

Observation gaps are reported as neutral review signals. They are not misconduct judgments.

## Deferred boundaries

Institutional management, hosted verification, IDE adapters, desktop UI, key hosting, and chain-preserving timeline compaction are separate future work. They must not become dependencies of the local workflow.
