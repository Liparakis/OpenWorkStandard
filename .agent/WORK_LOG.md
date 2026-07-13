## 2026-07-13 - Local-first core and Agent workflow

### Completed
- Established the local `init -> Agent -> package -> offline review` workflow.
- Kept project registration explicit and limited filesystem observation to initialized roots.

### Changed
- Added: local registry, Agent host, chained timeline, package hashing, offline verification, inspection, and reporting.
- Modified: CLI and documentation around the text-first workflow.
- Deleted: none recorded in this compact summary.

### Validation
- Build and tests were repeatedly run during implementation; the current release validation is recorded below.

### Remaining
- Windows setup lifecycle and final scope cleanup.

### Handoff
- Exact next action: validate the current local workflow before release review.
- Important context: OWS is a protocol/toolchain, not an LMS or SaaS platform.
- Files to inspect first: `README.md`, `docs/START_HERE.md`, `src/Ows.Core/Agent`, and `src/Ows.Core/Verification`.

## 2026-07-13 - Windows Agent setup lifecycle

### Completed
- Built the self-contained `Ows.Setup.exe` SCM installer, silent Agent host, uninstall entry, recovery actions, and shared-data choice.
- Owner smoke-tested install, service presence/running state, recovery configuration, uninstall, and installed-file cleanup.

### Changed
- Added: Windows setup/service boundary and native uninstall metadata.
- Modified: setup stop/delete ordering and service failure handling.
- Deleted: password-based and Scheduled Task bootstrap paths from the product path.

### Validation
- Owner confirmed the service lifecycle and post-uninstall absence of the service, install directory, and uninstall entry.

### Remaining
- Keep the Windows boundary documented without reintroducing old bootstrap compatibility.

### Handoff
- Exact next action: inspect `src/Ows.Setup/Program.cs` and the Windows build script.
- Important context: SCM install requires UAC; the service uses the shared machine registry.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `src/Ows.Setup/Ows.Setup.csproj`, and `scripts/windows/build-ows-setup.ps1`.

## 2026-07-13 - Documentation and repository surface prune

### Completed
- Reduced the root and documentation surface to active local-first product material.
- Removed sample, archive, deployment, pilot, operations, IDE, and management documentation that described unreleased scope.

### Changed
- Added: none.
- Modified: README, canonical docs, roadmap, release checklist, and continuity notes.
- Deleted: redundant root Markdown, stale docs, generated outputs, and tracked deployment/observability files.

### Validation
- Internal Markdown links and `git diff --check` passed; ignored outputs were removed.

### Remaining
- Reconcile code terminology and package verification with the reduced surface.

### Handoff
- Exact next action: search for stale remote/session/legacy names in source, tests, and notes.
- Important context: Unreleased compatibility is disposable.
- Files to inspect first: `.agent/DECISIONS.md`, `src/Ows.Core/Verification`, and `src/Ows.Cli`.

## 2026-07-13 - Remove unreleased CLI and hosted verifier stack

### Completed
- Removed hidden session/watch/event ceremony, receipts, notarization, hosted verifier code, PostgreSQL storage, auth/RBAC, deployment helpers, and server-only tests.
- Reduced the CLI to seven local commands and retained offline package verification as the contract.

### Changed
- Added: none.
- Modified: solution references, local verification/reporting, active docs, tests, and continuity notes.
- Deleted: unreleased remote/server source, scripts, deployment files, docs, and tests.

### Validation
- Build passed with 0 warnings/errors; Core 41/41 and CLI 10/10 passed after reconciliation.

### Remaining
- Remove any residual compatibility names and reconcile the handoff notes.

### Handoff
- Exact next action: audit `OwsProjectAgent`, package verification, setup migration paths, and the durable decisions.
- Important context: A remote verifier was an optional tamper anchor, not required by OWS v0.
- Files to inspect first: `src/Ows.Core/Agent`, `src/Ows.Core/Verification`, `src/Ows.Setup/Program.cs`, and `.agent/DECISIONS.md`.

## 2026-07-13 - Commit local-only legacy cleanup

### Completed
- Committed the cleanup as `de61aa7` and the handoff notes as `739251c`.
- Removed stale Agent/session names, unsigned-package compatibility state, setup migration paths, redundant one-implementation interfaces, and the remaining tracked deployment stack.

### Changed
- Added: the accepted local/offline tamper-detection decision.
- Modified: Agent naming, CLI JSON documentation, package verification terminology, setup lifecycle, and tests.
- Deleted: `deploy/`, obsolete interfaces, the old session-manager interface, and legacy compatibility paths.

### Validation
- Release build passed with 0 warnings/errors; full tests passed Core 41/41 and CLI 10/10.
- Local tamper suite passed 10/10; local CLI smoke path passed `init -> package -> verify -> inspect -> report`.
- PowerShell syntax passed for 2 scripts; Bash/WSL was unavailable; `git diff --check` and generated-output cleanup passed.

### Remaining
- Final CLI documentation/continuity reconciliation, then owner review.

### Handoff
- Exact next action: run release validation after the CLI JSON correction, commit the notes, and hand off.
- Important context: Hosted verification and anchoring are deferred future work.
- Files to inspect first: `src/Ows.Cli/Commands/InspectCommandBuilder.cs`, `docs/development/CLI.md`, and `.agent/DECISIONS.md`.

## 2026-07-13 - Final CLI and continuity reconciliation

### Completed
- Corrected the CLI documentation to match actual JSON support and removed the duplicate inspect option.
- Marked superseded hosted-verifier/bootstrap decisions explicitly and compacted continuity notes to current contributor-readable facts.

### Changed
- Added: replacement decisions for current Agent registration and package-signature behavior.
- Modified: `InspectCommandBuilder`, CLI docs, CLI JSON test cleanup, `DECISIONS.md`, `CURRENT_TASK.md`, and this log.
- Deleted: stale continuity history and false legacy claims; no product source deleted in this unit.

### Validation
- Release build passed with 0 warnings/errors; full tests passed Core 41/41 and CLI 10/10; focused inspect test passed 1/1.
- Release-binary smoke passed `init -> package -> verify -> inspect --json -> report --format json`.
- Markdown links, legacy-reference scans, `git diff --check`, and generated-output cleanup passed.

### Remaining
- Commit the final notes/docs/CLI correction and await owner review.

### Handoff
- Exact next action: commit the final note/docs/CLI correction, then hand off for owner review.
- Important context: Local hashes/signatures are the v0 tamper boundary; no server is required.
- Files to inspect first: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/DECISIONS.md`, and `git status`.

## 2026-07-13 - Commit final release reconciliation

### Completed
- Committed the final CLI/docs and continuity reconciliation as `624ed33`.

### Changed
- Added: none after the commit.
- Modified: continuity notes only for the post-commit handoff.
- Deleted: none after the commit.

### Validation
- Release build, full tests, release smoke workflow, Markdown link check, legacy-reference scan, and `git diff --check` passed.
- Generated build outputs were removed; the working tree is clean.

### Remaining
- Owner review and explicit publication authorization.

### Handoff
- Exact next action: owner reviews commits `de61aa7` and `624ed33` plus the Windows SCM lifecycle evidence.
- Important context: Local package integrity is the v0 tamper boundary; hosted anchoring is deferred.
- Files to inspect first: `.agent/NEXT_STEPS.md`, `.agent/DECISIONS.md`, `docs/development/CLI.md`, and `src/Ows.Core/Verification`.

## 2026-07-13 - Remove final active legacy terminology

### Completed
- Renamed the remaining snapshot continuity variable/message and ignore-rule test fixture from legacy wording to neutral unbound/additional wording.

### Changed
- Added: none.
- Modified: Agent continuity code, verification findings/docs, and the ignore-engine test.
- Deleted: none.

### Validation
- Full tests passed Core 41/41 and CLI 10/10.
- Release build passed with 0 warnings/errors.
- Active source/test/docs scan contains no legacy compatibility implementation names or `UnsignedLegacy`.

### Remaining
- Owner review and explicit publication authorization.

### Handoff
- Exact next action: owner reviews commit `6001de1`, then confirms the release candidate.
- Important context: Remaining server/session mentions are intentional negative-boundary documentation, not active features.
- Files to inspect first: `src/Ows.Core/Agent/LocalTrackingAgent.cs`, `src/Ows.Core/Verification/Helpers`, and `.agent/CURRENT_TASK.md`.

## 2026-07-13 - Commit final terminology correction

### Completed
- Committed the final active-code terminology cleanup as `6001de1`.

### Changed
- Added: none after the commit.
- Modified: continuity notes only for the post-commit handoff.
- Deleted: none after the commit.

### Validation
- Full tests 41/41 Core and 10/10 CLI; Release build 0 warnings/errors; legacy scan clean; generated outputs absent.

### Remaining
- Owner review and explicit publication authorization.

### Handoff
- Exact next action: owner reviews the local-only release candidate and Windows SCM lifecycle evidence.
- Important context: No hosted verifier or remote anchor is required by v0.
- Files to inspect first: `.agent/NEXT_STEPS.md`, `.agent/DECISIONS.md`, `README.md`, and `docs/core/SECURITY.md`.

## 2026-07-13 - Validate real Agent recording path

### Completed
- Ran a release-binary runtime smoke with an isolated registry: `ows init`, background `ows agent run`, file change, package, verify, and inspect.
- Confirmed the package contained a valid non-empty timeline with 8 events.

### Changed
- Added: none.
- Modified: continuity notes only.
- Deleted: temporary smoke project and Agent process after validation.

### Validation
- Release build passed with 0 warnings/errors; Agent smoke passed; no temporary Agent process or project remains.

### Remaining
- Owner review and explicit publication authorization.

### Handoff
- Exact next action: owner reviews the local-only release candidate and the runtime Agent evidence.
- Important context: The Agent records project evolution without requiring a server or manual checkpoint ceremony.
- Files to inspect first: `src/Ows.Core/Agent/OwsAgentHost.cs`, `src/Ows.Core/Agent/OwsProjectAgent.cs`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 - Remove dead CLI trust field

### Completed
- Confirmed `OwsCliResponse.TrustStatus` had no writer or consumer and removed it from the response model and JSON shape.

### Changed
- Added: a JSON regression assertion that `TrustStatus` is absent.
- Modified: `src/Ows.Cli/OwsCliResponse.cs` and `CliJsonProtocolTests.cs`.
- Deleted: the unused response property and serialized field.

### Validation
- Focused CLI JSON test passed 1/1.
- Release build passed with 0 warnings/errors; full suite remained green at Core 41/41 and CLI 10/10.

### Remaining
- None for this fix; owner review/publication gate remains unchanged.

### Handoff
- Exact next action: commit this dead-field cleanup.
- Important context: Verification trust remains correctly sourced from `Ows.Core.VerificationResult`; it was never part of the project-state CLI response.
- Files to inspect first: `src/Ows.Cli/OwsCliResponse.cs` and `tests/Ows.Cli.Tests/CliJsonProtocolTests.cs`.

## 2026-07-13 - Audit dead-field commit

### Completed
- Audited commit `6856367` and confirmed the requested dead-field removal is present.
- Preserved unrelated CLI cleanup already captured in that commit and left the separate `OwsCommandFactory.cs` working-tree edit untouched.

### Changed
- Added: continuity updates only.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: none.

### Validation
- Build: Release build passed with 0 warnings/errors.
- Targeted tests: CLI JSON regression passed.
- Full tests: Core 41/41 and CLI 10/10 passed.
- Manual checks: `git diff --check` passed; `git clean -ndX` showed only generated outputs and `.idea/`.

### Remaining
- Owner review of `6856367` and the separate uncommitted `OwsCommandFactory.cs` edit.

### Handoff
- Exact next action: review the dead-field commit and decide whether to retain the separate factory edit before the next commit.
- Important context: Core verification trust status remains active and is used by package verification, inspect, and reports.
- Files to inspect first: `src/Ows.Cli/OwsCliResponse.cs`, `tests/Ows.Cli.Tests/CliJsonProtocolTests.cs`, and `src/Ows.Cli/OwsCommandFactory.cs`.

## 2026-07-13 - Remove dead snapshot accessor

### Completed
- Traced `LoadSnapshotResult` consumers and confirmed `HadSnapshotFile` had no caller.
- Removed the unused property and object-initializer assignment.

### Changed
- Added: none.
- Modified: `src/Ows.Core/Agent/Snapshot/ObservedSnapshotStore.cs` and continuity notes.
- Deleted: `LoadSnapshotResult.HadSnapshotFile`.

### Validation
- Build: Release build passed with 0 warnings/errors.
- Targeted tests: Core tests passed 41/41.
- Full tests: Core 41/41 and CLI 10/10 passed.
- Manual checks: no source/test references remain; `git diff --check` passed.

### Remaining
- None for this cleanup; unrelated Agent/scanning changes remain unstaged for separate review.

### Handoff
- Exact next action: owner reviews commit `6d7b7e6`; keep the unrelated Agent/scanning changes separate.
- Important context: The local `hadSnapshotFile` variable remains required to detect and parse an existing snapshot; only the unused result accessor was removed.
- Files to inspect first: `src/Ows.Core/Agent/Snapshot/ObservedSnapshotStore.cs` and `git status --short`.
