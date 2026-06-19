# OWS Project Status

Last updated: 2026-06-19

## Summary

OWS is past the bootstrap stage and now has a working local MVP slice in the .NET solution:

- project initialization works
- timeline capture exists, but only as a one-shot filesystem scan
- package creation works and produces real `.owspkg` archives
- package verification works and checks structure, content integrity, and trust grading
- text report generation works and includes trust status
- core and CLI tests are in place and passing

The main weakness is tracking. `ows watch` is not a persistent watcher yet. It does one scan of the current project and appends `FileCreated` events for existing files.

## Current Solution Shape

Source projects:

- `src/Ows.Core`
- `src/Ows.Cli`
- `src/Ows.Desktop`

Test projects:

- `tests/Ows.Core.Tests`
- `tests/Ows.Cli.Tests`

Notes:

- the earlier fragmented source projects were collapsed into `Ows.Core` namespaces
- `Ows.Desktop` is still a placeholder project

## Implemented Capabilities

### `ows init`

What works:

- creates the local `.ows/` folder
- creates `.ows/config.json`
- creates `.ows/timeline.jsonl`

Status:

- usable
- minimal

### `ows watch`

What works:

- prepares the local tracking agent
- scans the project tree once
- skips files under `.ows/`
- appends `FileCreated` events to `.ows/timeline.jsonl`

What does not work yet:

- no long-running watcher
- no automatic background tracking
- no platform-specific host integration
- no tamper-evident chain yet

Status:

- functional proof of concept
- not a real watcher

### `ows package`

What works:

- creates a real `.owspkg` zip archive
- writes `manifest.json`
- writes `timeline.jsonl`
- writes `version_graph.json`
- includes project files under `artifacts/`
- excludes `.ows/` content from packaged artifacts
- excludes the output package itself from packaged artifacts
- computes and stores hashes for timeline, version graph, and every packaged artifact

Status:

- working MVP implementation

### `ows verify`

What works:

- checks package existence
- checks required archive entries
- validates manifest JSON
- validates timeline JSONL line by line
- validates version graph JSON
- validates timeline hash against manifest
- validates version graph hash against manifest
- validates each packaged artifact hash against manifest
- rejects undeclared extra `artifacts/` entries

Status:

- working MVP implementation
- strongest area of the current codebase

### `ows report`

What works:

- runs verification first
- generates a text report from verification output
- writes `<project>.report.txt`

Status:

- working MVP implementation
- still basic in format and depth

## Core Domains Already Present

`Ows.Core` currently contains these namespaces and working building blocks:

- `Ows.Core.Agent`
- `Ows.Core.Events`
- `Ows.Core.Graph`
- `Ows.Core.Hashing`
- `Ows.Core.Init`
- `Ows.Core.Packaging`
- `Ows.Core.Reporting`
- `Ows.Core.Notarization`
- `Ows.Core.Verification`

This is the right collapsed shape for the MVP. No extra project split is needed right now.

## Testing Status

Current automated coverage exists for:

- project initialization
- one-shot watch scan behavior
- package creation
- package verification success and failure cases
- report generation
- command construction and CLI command behavior
- serialization and hashing basics
- version graph primitives

Recent verified commands:

```powershell
dotnet build
dotnet test
```

Both were passing at the latest implementation checkpoints before this document was written.

## Recent Progress

Recent milestones from git history:

- `62b3507` `chore: initialize OWS .NET solution structure`
- `7e85a76` `refactor: collapse MVP project structure`
- `a6eb0b8` `feat: implement minimal OWS CLI workflow`
- `e03aa22` `feat: validate package version graph json`
- `7121b9c` `feat: verify package content hashes`
- `39d645f` `feat: include project artifacts in packages`
- `e26e6ce` `feat: verify packaged artifact hashes`
- `0003f21` `feat: reject undeclared package artifacts`

Net result:

- the repository moved from honest placeholders to a thin but real end-to-end local workflow

## Current Gaps

The biggest missing pieces are:

- persistent watcher lifecycle
- platform-specific host integrations for VS Code, Rider, and desktop
- local tamper-evident event chaining
- receipt-aware remote verification
- stronger report output
- desktop UI beyond placeholder state

## Reality Check

Some existing docs are stale relative to the code:

- `README.md` still describes the repository as mostly bootstrap-stage
- `docs/CLI.md` still says the commands are placeholders

That is no longer true for `init`, `package`, `verify`, and `report`. It is only partly true for `watch`.

## Recommended Next Steps

If progress stays MVP-focused, the next worthwhile steps are:

1. add tamper-evident event chaining to the local timeline
2. scaffold the remote verifier boundary instead of growing the fake watcher first
3. add receipt-aware verification so `Verified` means something concrete
4. leave `Ows.Desktop` as a placeholder until tracking architecture is settled

## Bottom Line

OWS currently has a credible local MVP for:

- initializing project state
- capturing a minimal timeline snapshot
- packaging evidence
- verifying package integrity
- producing a basic report

It does not yet have a credible always-on tracking model or a real remote verifier, but it now has the first trust-grading and receipt-model foundation for that direction.
