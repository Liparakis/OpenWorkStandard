# Work Log

## 2026-07-13 — Continuity setup and repository reconciliation

### Completed
- Read repository instructions, roadmap, project status, existing product docs, and current git state.
- Confirmed the repository had no `.agent` continuity files.
- Identified the active roadmap direction as Public Alpha Truth Audit and Release Candidate v0.1.
- Recorded the pre-existing education-management scope reduction as an owner-review risk.

### Changed
- Added: `.agent/CURRENT_TASK.md`, `.agent/DECISIONS.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Modified: None outside `.agent/`.
- Deleted: None.

### Validation
- Build: Not run yet.
- Targeted tests: Not run yet.
- Full tests: Not run yet.
- Manual checks: Repository status and diff inspected; working-tree changes preserved.

### Remaining
- Run build, full tests, and whitespace validation against the current tree.
- Reconcile or explicitly approve the education-layer removal before source changes.

### Handoff
- Exact next action: Run `dotnet build OWS.sln -nologo`, `dotnet test OWS.sln -nologo`, and `git diff --check`.
- Important context: The source diff predates these notes and is intentionally untouched.
- Files to inspect first: `OWS.sln`, `src/Ows.Core/Notarization/Migrations/PostgresVerifierMigrator.cs`, `src/Ows.Verifier.Server/`, and education references under `src`, `tests`, and `docs`.

## 2026-07-13 — Continuity validation and handoff

### Completed
- Ran the required build, full test command, and whitespace validation.
- Confirmed the Core test assembly passes 112/112 tests.
- Confirmed the verifier-server compile failure is caused by remaining `Ows.Core.Education` and `IEducationStore` references after the existing education-layer deletion.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Failed with five verifier-server compile errors in `VerifierSessionEndpoints.cs`, `PackageVerificationWorker.cs`, and `VerifierAuthorizationHelpers.cs`.
- Targeted tests: Core tests passed, 112/112.
- Full tests: Failed during solution build after Core tests passed; no overall green result.
- Manual checks: `git diff --check` passed with only line-ending conversion warnings.

### Remaining
- Resolve owner intent for the education-management removal before changing source or docs.
- Reconcile remaining references and rerun the release gate after that decision.

### Handoff
- Exact next action: Ask the owner to confirm whether the education-layer removal should be completed or reverted.
- Important context: No source files outside `.agent/` were changed by this task; all existing source changes remain untouched.
- Files to inspect first: `src/Ows.Verifier.Server/Endpoints/VerifierSessionEndpoints.cs`, `src/Ows.Verifier.Server/Packages/PackageVerificationWorker.cs`, `src/Ows.Verifier.Server/Helpers/VerifierAuthorizationHelpers.cs`, and the deleted `src/Ows.Core/Education/` files.

## 2026-07-13 — Education-layer owner-review audit

### Completed
- Traced all remaining `Ows.Core.Education`, `IEducationStore`, and store-backed report/session references.
- Confirmed the working-tree removal covers the education domain, stores, education routes, registrations, migrations, and dedicated education tests.
- Identified the remaining compile blockers and stale tests/docs.
- Prepared the recommendation to complete removal while retaining opaque scope identifiers.

### Changed
- Added: Pending disposition entry in `.agent/DECISIONS.md`.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/DECISIONS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Existing five verifier-server compile errors remain; no source repair attempted.
- Targeted tests: Core tests previously passed 112/112; no new tests run.
- Full tests: Not rerun; source is unchanged.
- Manual checks: Direct structural scan completed. Graphify could not complete semantic extraction because no supported LLM key was configured for 63 docs.

### Remaining
- Obtain owner approval for the recommended removal disposition.
- If approved, complete the minimal metadata-only refactor and documentation/test reconciliation.

### Handoff
- Exact next action: Owner responds whether to accept the pending education-management removal disposition.
- Important context: The recommended path is remove management resolution, keep opaque scope IDs, and do not restore deleted domain/store/routes.
- Files to inspect first: `src/Ows.Verifier.Server/Endpoints/VerifierSessionEndpoints.cs`, `src/Ows.Verifier.Server/Packages/PackageVerificationWorker.cs`, `src/Ows.Verifier.Server/Helpers/VerifierAuthorizationHelpers.cs`, and `src/Ows.Core/Verification/VerificationResult.cs`.

## 2026-07-13 — Metadata-only verifier refactor

### Completed
- Removed remaining runtime dependencies on `Ows.Core.Education` and `IEducationStore`.
- Changed session and package verification paths to preserve only opaque external context identifiers.
- Removed management-only rate-limit policies and education route authorization logic.
- Replaced management-specific integration tests with focused verifier scope tests.
- Reconciled README, API, data model, roadmap, operations docs, pilot fixtures, and readiness helpers.

### Changed
- Added: Metadata-only pilot fixture behavior and scoped auth test coverage.
- Modified: Verifier session/worker/auth code, report renderers, operational scripts, and active documentation.
- Deleted: Education domain/store/endpoint/test artifacts already present in the working tree; obsolete management assertions.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed, 0 warnings and 0 errors.
- Targeted tests: CLI/server tests passed, 78/78.
- Full tests: Core tests passed, 112/112; full solution test command passed.
- Manual checks: Active-source stale-reference scan found no `/education`, `IEducationStore`, `Ows.Core.Education`, or `educationStoreReady` references; `git diff --check` passed with line-ending warnings only.

### Remaining
- Validate modified PowerShell/Bash helper syntax.
- Run the release regression gate and inspect its evidence for stale assumptions.

### Handoff
- Exact next action: Run script parser checks, then the release regression gate if local prerequisites are available.
- Important context: OWS retains opaque context IDs for scoping and reporting but owns no education-management records or routes.
- Files to inspect first: `scripts/windows/run-live-pilot-dry-run.ps1`, `scripts/unix/run-live-pilot-dry-run.sh`, `scripts/windows/setup-pilot-fixture.ps1`, and `scripts/unix/setup-pilot-fixture.sh`.

## 2026-07-13 — Release-gate validation and handoff

### Completed
- PowerShell parser checks passed for the modified Windows operational helpers.
- Release gate restore, build, full tests, and VS Code compile steps passed.
- Confirmed the live pilot failure is environmental: Docker Desktop's Linux daemon is unavailable, so PostgreSQL could not start.
- Confirmed active source/docs/scripts contain no stale `/education`, `IEducationStore`, `Ows.Core.Education`, or `educationStoreReady` references.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Passed, 0 warnings and 0 errors.
- Targeted tests: Core 112/112 and CLI/server 78/78 passed.
- Full tests: Passed.
- Manual checks: `git diff --check` passed with line-ending warnings only; PowerShell syntax passed; Bash syntax unavailable on this host.
- Release gate: Failed at live pilot startup only; `artifacts/release-gate/release-gate-summary.json` records passed restore/build/test/VS Code stages and the Docker/PostgreSQL blocker.

### Remaining
- Rerun the live gate after Docker/PostgreSQL is available.
- Validate Unix helper syntax in WSL or macOS/Linux.
- Refresh release-candidate evidence and complete manual sign-off.

### Handoff
- Exact next action: Start Docker Desktop's Linux daemon or configure a reachable PostgreSQL, then rerun the Windows release gate.
- Important context: The code and docs refactor is complete and test-green; only live infrastructure validation remains.
- Files to inspect first: `artifacts/release-gate/release-gate-summary.json`, `scripts/windows/run-release-regression-gate.ps1`, and `scripts/windows/run-live-pilot-dry-run.ps1`.

## 2026-07-13 — Cold-start release gate and verifier lifecycle hardening

### Completed
- Started Docker Desktop and reproduced the initial PostgreSQL migration race from a cold container.
- Added bounded PostgreSQL health readiness waits to Windows and Unix foreground/background verifier helpers.
- Fixed Windows shutdown to terminate the verifier process tree and safely clean orphaned workspace verifier processes.
- Renamed the VS Code command and pilot evidence field to `External Context`.
- Re-ran the cold-start release gate and cut a fresh v0.1 evidence bundle.

### Changed
- Added: Shared PostgreSQL readiness and Windows verifier process-tree cleanup helpers.
- Modified: Local verifier lifecycle scripts, VS Code command title, pilot evidence fields, release status docs, and continuity notes.
- Deleted: None.

### Validation
- Build: Release gate passed.
- Targeted tests: CLI/server 78/78 passed.
- Full tests: Core 112/112 passed; full solution test command passed.
- Manual checks: PowerShell syntax passed; Bash syntax passed in temporary Docker `bash:5.2`; active stale-reference scan empty; `git diff --check` passed with line-ending warnings only.
- Cleanup: PostgreSQL container was stopped after evidence collection; port 5432 is clear.
- Release gate: Passed from cold PostgreSQL startup through migrations, compose validation, live pilot, package verification, reviewer denial, request-id logging, and secret-leak checks.
- Evidence: `artifacts/release-candidate/v0.1/` generated with `ReadyForManualSignoff` status.

### Remaining
- VS Code trusted-workspace smoke check if required by operator review.
- Operator release-candidate approval and Public Alpha Truth Audit.

### Handoff
- Exact next action: Review `artifacts/release-candidate/v0.1/` and record operator sign-off.
- Important context: The cold-start race and orphaned verifier lifecycle bugs are fixed and verified by the green gate.
- Files to inspect first: `artifacts/release-candidate/v0.1/evidence-manifest.json`, `docs/development/RELEASE_CHECKLIST.md`, and `.agent/NEXT_STEPS.md`.


## 2026-07-13 — External context naming cleanup

### Completed
- Renamed the public report/request context types and properties from education-specific names to `ReportExternalContext` and `ExternalContext`.
- Kept the wire report field as neutral `context` and preserved opaque identifier values.

### Changed
- Added: None.
- Modified: `OwsPackageVerifier`, verification DTOs, package worker, and text/JSON report renderers.
- Deleted: None.

### Validation
- Build: Passed, 0 warnings and 0 errors.
- Targeted tests: CLI/server 78/78 passed.
- Full tests: Core 112/112 passed; full solution test command passed.
- Manual checks: No remaining active references to `ReportEducationContext`, `EducationContext`, `.Education`, `/education`, or `IEducationStore` outside archived historical docs.

### Remaining
- Live release-gate validation still requires Docker/PostgreSQL.
- Unix helper syntax validation still requires Bash/WSL/Linux.

### Handoff
- Exact next action: Start Docker/PostgreSQL and rerun the release gate.
- Important context: The codebase now names external context as external context; OWS owns no education-management model.
- Files to inspect first: `src/Ows.Core/Verification/VerificationResult.cs`, `src/Ows.Core/Verification/PackageVerificationRequest.cs`, `src/Ows.Core/Reporting/Renderers/JsonReportRenderer.cs`, and `src/Ows.Verifier.Server/Packages/PackageVerificationWorker.cs`.

## 2026-07-13 — Active documentation truth audit

### Completed
- Reconciled the active infrastructure guide with the metadata-only verifier boundary.
- Reconciled the pilot walkthrough with fixture scripts that create opaque identifiers and API keys, not management records.
- Clarified that VS Code watch start/stop commands are explicit smoke-test controls, not routine student ceremony.

### Changed
- Added: None.
- Modified: `docs/development/INFRASTRUCTURE.md`, `docs/workflows/PILOT_DEMO.md`, `docs/integrations/VS_CODE_EXTENSION.md`, and continuity notes.
- Deleted: None.

### Validation
- Build: Existing cold-start release gate passed; full build recheck remains part of final handoff.
- Targeted tests: Existing CLI/server 78/78 passed.
- Full tests: Existing Core 112/112 passed.
- Manual checks: Active-doc stale-ownership scan passed; `git diff --check` passed with line-ending warnings only.

### Remaining
- Review release-candidate evidence and complete operator-owned VS Code smoke/sign-off checks.

### Handoff
- Exact next action: Inspect `artifacts/release-candidate/v0.1/evidence-manifest.json`, then run the final build/test validation.
- Important context: OWS retains opaque external context values for scoping and reporting but does not create or resolve institutional management records.
- Files to inspect first: `artifacts/release-candidate/v0.1/evidence-manifest.json`, `docs/development/RELEASE_CHECKLIST.md`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Phase 2 IDE drift removal

### Completed
- Removed the tracked VS Code extension and Rider/IntelliJ integration designs from active OWS scope.
- Removed the IDE compile and interactive IDE checks from the release gate and release-candidate evidence.
- Rewrote active student, pilot, operations, event, roadmap, and project-status docs around CLI/Core and generic host boundaries.

### Changed
- Added: Accepted decision that future IDE adapters are separate projects outside OWS core.
- Modified: Release scripts, active documentation, `EventCommandBuilder`, and continuity notes.
- Deleted: `src/ows-vscode/`, `docs/integrations/VS_CODE_EXTENSION.md`, `docs/integrations/RIDER_INTEGRATION.md`, and `docs/integrations/IDE_INTEGRATION.md`.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: No new Phase 2 test code; release-gate test step passed.
- Full tests: Core 112/112 and CLI/server 78/78 passed.
- Manual checks: PowerShell syntax passed; Bash syntax passed in Docker `bash:5.2`; active IDE-reference scan left only deliberate boundary/checklist text; `git diff --check` passed.
- Release gate: Passed without IDE compilation; cold/local verifier, compose validation, package verification, reviewer denial, request-id logging, and secret-leak checks passed.
- Evidence: `artifacts/release-candidate/v0.1/` refreshed with only operator sign-off remaining.

### Remaining
- Inspect and implement the centralized `.owsignore` behavior for Phase 3.

### Handoff
- Exact next action: Search `OwsProjectInitializer`, observation/scanning, and packaging code for existing exclusion logic.
- Important context: OWS is now CLI/Core and filesystem-first in active scope; generic host metadata is allowed, but no IDE adapter is part of the release path.
- Files to inspect first: `src/Ows.Core/Init/OwsProjectInitializer.cs`, `src/Ows.Core/Observation/`, `src/Ows.Core/Packaging/`, and `tests/Ows.Core.Tests/`.

## 2026-07-13 — Phase 3 `.owsignore` implementation

### Completed
- Added a centralized `OwsIgnoreEngine` with default rules, comments, blank lines, directory patterns, simple wildcards, root-relative paths, and separator normalization.
- Updated `ows init` to create `.owsignore` only when it does not already exist.
- Wired the same loaded rules into recovery scans and native/polling watcher exclusion decisions.
- Documented the supported subset and default exclusions.

### Changed
- Added: `src/Ows.Core/Ignore/OwsIgnoreEngine.cs` and `tests/Ows.Core.Tests/OwsIgnoreEngineTests.cs`.
- Modified: `OwsProjectInitializer`, `ProjectFileScanner`, `LocalTrackingAgent`, CLI/student docs, README, roadmap, and initializer tests.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Ignore engine and initializer tests passed, 5/5.
- Full tests: Core 116/116 and CLI/server 78/78 passed.
- Manual checks: PowerShell syntax passed; Bash syntax passed in Docker `bash:5.2`; active education/IDE stale-reference scan passed with only deliberate boundary/checklist/default-rule text; `git diff --check` passed.

### Remaining
- Make package artifact collection use the same ignore engine and add archive-level exclusion/inclusion tests.

### Handoff
- Exact next action: Update `PackageArtifactCollector`/`OwsPackageBuilder` to load `.owsignore`, then test excluded secrets/build/dependency paths and included text/binary artifacts.
- Important context: `.owsignore` is intentionally smaller than `.gitignore`; negation and full precedence semantics are not supported.
- Files to inspect first: `src/Ows.Core/Packaging/Helpers/PackageArtifactCollector.cs`, `src/Ows.Core/Packaging/OwsPackageBuilder.cs`, and `tests/Ows.Core.Tests/PackagingNamespaceTests.cs`.


## 2026-07-13 — Phase 4 packaging safety

### Completed
- Wired `PackageArtifactCollector` and `OwsPackageBuilder` to the shared `OwsIgnoreEngine`.
- Excluded default/user ignore paths and configured watcher directories from artifact hashes and archive entries.
- Preserved `.owsignore` as a visible project text artifact and preserved explicitly included binaries as opaque hashed entries.
- Documented package exclusions and added archive-level regression coverage.

### Changed
- Added: Package exclusion/inclusion and local verification coverage in `PackagingNamespaceTests`.
- Modified: `PackageArtifactCollector`, `OwsPackageBuilder`, `PACKAGE_FORMAT.md`, roadmap, and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Packaging tests passed, 4/4.
- Full tests: Core 117/117 and CLI/server 78/78 passed.
- Manual checks: PowerShell syntax passed; Bash syntax passed in Docker `bash:5.2`; active education/IDE implementation-reference scan passed; `git diff --check` passed.
- Package check: Ignored secrets/build/dependency/log paths were absent; text and binary artifacts remained; local package verification passed.

### Remaining
- Design and implement the smallest installable always-on local-agent/service slice for Phase 5.

### Handoff
- Exact next action: Inspect `OwsWatchSessionManager`, `Ows.Cli` init/watch commands, project initialization, and existing watcher state files for registry and IPC seams.
- Important context: The shared ignore engine now governs both tracking and packaging; Phase 5 must remove routine manual watch ceremony without adding a network-facing local API.
- Files to inspect first: `src/Ows.Core/Agent/OwsWatchSessionManager.cs`, `src/Ows.Cli/Commands/WatchCommandBuilder.cs`, `src/Ows.Cli/Commands/InitCommandBuilder.cs`, and `src/Ows.Core/Init/OwsProjectInitializer.cs`.

## 2026-07-13 — Phase 5 agent lifecycle audit

### Completed
- Audited `ows init`, `ows watch`, package coordination, watcher PID state, and `OwsWatchSessionManager`.
- Confirmed the current implementation is project-local and single-watcher: it has `.ows/watcher.json` and `.ows/watcher.stop`, but no project registry, always-on host, or local IPC.
- Preserved the existing diagnostic watch commands while defining the safe next seam.

### Changed
- Added: Phase 5 continuity scope and handoff notes.
- Modified: roadmap/current/next continuity state and roadmap status wording.
- Deleted: None.

### Validation
- Build: Phase 4 build remains green with 0 warnings and 0 errors.
- Targeted tests: Phase 4 packaging tests passed 4/4.
- Full tests: Phase 4 Core 117/117 and CLI/server 78/78 passed.
- Manual checks: Phase 4 syntax, stale-reference, and whitespace checks passed.

### Remaining
- Choose and implement a secure local registry, agent host, and IPC boundary before removing routine watcher ceremony.

### Handoff
- Exact next action: Design the smallest portable registry/host abstraction and Windows-first service boundary, with tests for registration, restart recovery, deleted roots, multiple projects, and agent unavailability.
- Important context: This phase changes process lifecycle and local authorization; keep it explicit, reversible, and separate from the verifier server.
- Files to inspect first: `src/Ows.Core/Agent/OwsWatchSessionManager.cs`, `src/Ows.Cli/Commands/InitCommandBuilder.cs`, `src/Ows.Cli/Commands/WatchCommandBuilder.cs`, and `src/Ows.Core/Agent/Watcher/WatcherStateStore.cs`.

## 2026-07-13 — Phase 5 registry and host foundation

### Completed
- Added a user-local JSON `OwsProjectRegistry` with atomic writes, cross-process lock file, idempotent registration, and missing-root pruning.
- Added `OwsAgentHost` to run the existing watcher manager across all registered initialized projects and recover them after host restart.
- Added `ows agent run` and made `ows init` register the current project root.

### Changed
- Added: `OwsProjectRegistry`, `OwsAgentHost`, `AgentCommandBuilder`, registry/host tests, and CLI init registration coverage.
- Modified: `OwsCommandFactory`, `InitCommandBuilder`, CLI command-surface tests, CLI docs, project status, roadmap, decisions, and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Agent registry/host 2/2 and CLI init registration 1/1 passed.
- Full tests: Core 119/119 and CLI/server 78/78 passed.
- Manual checks: Existing syntax, stale-reference, and diff checks remain green.

### Remaining
- Secure local IPC, agent-unavailable/package coordination, and reversible OS service/host installation remain before Phase 5 completion.

### Handoff
- Exact next action: Design local IPC around the existing registry/host without adding HTTP; then add service lifecycle tests.
- Important context: `ows agent run` is a tested host foundation, not yet an installable always-on service.
- Files to inspect first: `src/Ows.Core/Agent/OwsAgentHost.cs`, `src/Ows.Core/Agent/OwsProjectRegistry.cs`, `src/Ows.Cli/Commands/AgentCommandBuilder.cs`, and `src/Ows.Cli/Commands/PackageCommandBuilder.cs`.

## 2026-07-13 — Phase 5 local IPC and Windows service boundary

### Completed
- Added current-user local IPC using Windows named pipes and Unix-domain sockets with ping/flush coordination.
- Added Agent-unavailable package fallback and explicit `AgentUnavailable` initialization status.
- Added a real Windows Services host, user-scoped install/uninstall/status scripts, and restart-compatible service lifecycle documentation.
- Corrected Windows verifier helper handling for normal Docker Compose stderr and made the release-gate success exit explicit.

### Changed
- Added: `OwsAgentIpc`, Windows service host registration, `install-ows-agent-service.ps1`, `uninstall-ows-agent-service.ps1`, and `get-ows-agent-service-status.ps1`.
- Modified: CLI init/package/agent commands, CLI hosting dependency, Phase 5 docs, roadmap, README, and verifier helper scripts.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Agent/IPC lifecycle tests passed 2/2; init registration coverage passed.
- Full tests: Core 119/119 and CLI/server 78/78 passed.
- Manual checks: PowerShell syntax passed for 18 scripts; Docker Bash syntax checks exited 0; `git diff --check` passed.
- Release gate: Summary passed for restore, build, tests, Compose validation, and live pilot dry run; verifier container stopped afterward.

### Remaining
- Phase 6 package root hashing, deterministic sealing, local signing, and offline signature verification.
- Linux/macOS service adapters remain deferred.

### Handoff
- Exact next action: Inspect `OwsPackageBuilder`, `OwsPackageVerifier`, manifest serialization, and existing trust states before defining canonical signed bytes.
- Important context: OWS remains local-first; unsigned packages must remain structurally valid and explicitly distinguishable from signed packages.
- Files to inspect first: `src/Ows.Core/Packaging/OwsPackageBuilder.cs`, `src/Ows.Core/Verification/OwsPackageVerifier.cs`, `src/Ows.Core/Verification/VerificationResult.cs`, and `docs/core/PACKAGE_FORMAT.md`.

## 2026-07-13 — Phase 6 package root hashing and signing

### Completed
- Added canonical `OWS-PACKAGE-ROOT-V1` logical bytes and a manifest package-root hash independent of ZIP order/timestamps.
- Added optional local RSA-SHA256 package signing with user-local key storage and public-key metadata.
- Added offline signature verification with explicit `Valid`, `Unsigned`, `UnsignedLegacy`, and `Invalid` signature states.
- Preserved existing receipt/continuity trust grades and unsigned package compatibility.

### Changed
- Added: package signature models, signing key store/signer, root canonicalizer, signature verifier, and signing/tamper tests.
- Modified: manifest, package builder/archive writer, verifier result/report renderers, CLI `--sign`, package/threat/glossary/roadmap/status docs, and README.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Package signing/tamper tests passed 7/7.
- Full tests: Core 126/126 and CLI/server 78/78 passed.
- Manual checks: Signed package verified after archive reordering; unsigned package verified with explicit unsigned status; required artifact/timeline/manifest/signature/removed/injected tamper cases rejected.

### Remaining
- Phase 7 public command/code-surface reduction.
- Linux/macOS service adapters and signing-key rotation/revocation remain deferred.

### Handoff
- Exact next action: Inventory the CLI command tree and active repository references before removing any public surface.
- Important context: Keep local `ows init`, `ows package`, and `ows verify`; treat remote verifier operations as optional until dependency evidence says otherwise.
- Files to inspect first: `src/Ows.Cli/OwsCommandFactory.cs`, `src/Ows.Cli/Commands/*.cs`, `docs/development/CLI.md`, and `tests/Ows.Cli.Tests/`.

## 2026-07-13 — Phase 7 command-surface reduction slice

### Completed
- Added reviewer-focused local `ows inspect` with JSON and text output for package root, signature state, timeline, artifacts, findings, and errors.
- Rewrote the student workflow around `ows init` and `ows package`; watcher ceremony and remote operations are explicitly optional diagnostics.
- Classified existing session/checkpoint/upload/event/watch surfaces as pilot or diagnostic dependencies instead of deleting them speculatively.

### Changed
- Added: `InspectCommandBuilder` and CLI inspection coverage.
- Modified: command factory, CLI reference, student workflow, README/status/roadmap references, and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: `ows inspect --json` CLI test passed 1/1.
- Full tests: Core 126/126 and CLI/server 79/79 passed.
- Manual checks: CLI help exposes `inspect`; `git diff --check` passed.

### Remaining
- Complete the dependency review before removing any remote pilot command surface.
- Phase 8 documentation/wiki polish remains after Phase 7 closure.

### Handoff
- Exact next action: trace session/checkpoint/upload/event/watch references in pilot scripts/tests, then decide whether any can be hidden, moved, or removed without breaking the pilot.
- Important context: Do not mistake “not student-routine” for “dead”; the live verifier pilot currently exercises several remote commands.
- Files to inspect first: `src/Ows.Cli/Commands/SessionCommandBuilder.cs`, `src/Ows.Cli/Commands/WatchCommandBuilder.cs`, `src/Ows.Cli/Commands/EventCommandBuilder.cs`, and `docs/workflows/PILOT_DEMO.md`.

## 2026-07-13 — Phase 8/9 documentation and release candidate audit

### Completed
- Added root contributor entry points: getting started, specification map, CLI reference, Agent design, security, contributing, code of conduct, and release guidance.
- Added CHANGELOG.md, GitHub issue templates, pull-request template, and release-readiness report.
- Updated architecture and student workflow docs to reflect the local Agent, offline verification, package root/signatures, and optional remote verifier.
- Renamed active migration symbols from stale education/enrollment terminology to protocol-neutral package-context/compatibility names without changing migration versions or columns.

### Changed
- Added: root release/contributor docs, .github templates, CHANGELOG.md, and RELEASE_READINESS.md.
- Modified: README, START_HERE, architecture, security, student workflow, roadmap/status docs, migration metadata, and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Full tests: Core 126/126 and CLI/server 79/79 passed.
- Release gate: restore, build, tests, Compose validation, and live pilot dry run passed.
- Manual checks: PowerShell syntax passed for 18 scripts; Bash syntax check exited 0; `git diff --check` passed; verifier ports clear after cleanup.

### Remaining
- Credentialed Windows Agent service install/status/uninstall smoke check by the owner.
- Manual repository history/license/generated-artifact review and release sign-off.

### Handoff
- Exact next action: Review docs/development/RELEASE_READINESS.md, then run the documented service lifecycle smoke check with the intended user account.
- Important context: No tag, push, GitHub release, or external publication was performed.
- Files to inspect first: `docs/development/RELEASE_READINESS.md`, `scripts/windows/install-ows-agent-service.ps1`, `scripts/windows/uninstall-ows-agent-service.ps1`, and `LICENSE`.

## 2026-07-13 — Release candidate sample and logical-root reproducibility

### Completed
- Added the tracked text-first samples/minimal-project fixture.
- Excluded generated package ID and timestamp from canonical root bytes so repeated builds of the same project retain the same logical root.
- Added a regression test for repeated-build root stability.

### Changed
- Added: samples/minimal-project.
- Modified: PackageRootCanonicalizer, package signing tests, getting-started/release-readiness continuity references.
- Deleted: None.

### Validation
- Build: Previously passing with 0 warnings and 0 errors; focused rebuild passed.
- Targeted tests: Package signing/reproducibility tests passed 8/8.
- Full tests: Final rerun passed Core 127/127 and CLI/server 79/79.
- Manual checks: Release gate already passed before this isolated canonical-root/sample change.

### Remaining
- Final full build/test and owner-only credentialed Windows service smoke check.

### Handoff
- Exact next action: Run the final full build/test, script syntax, stale-scope, and diff checks; then leave the release candidate uncommitted for owner review.
- Important context: No publication or service installation has been performed.
- Files to inspect first: `.agent/CURRENT_TASK.md`, `samples/minimal-project/README.md`, `src/Ows.Core/Packaging/PackageRootCanonicalizer.cs`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Final release-candidate validation

### Completed
- Reconciled final test counts and release-readiness evidence after the sample/root-stability change.
- Confirmed required release/contributor/security files and sample fixture exist.
- Confirmed no active OAP/education implementation or IDE integration references remain in the scoped source/docs scan.
- Confirmed no Docker containers or verifier ports remain running.

### Changed
- Added: final evidence updates in continuity and release-readiness notes.
- Modified: None beyond the already validated release-candidate tree.
- Deleted: None.

### Validation
- Build: 0 warnings and 0 errors.
- Full tests: Core 127/127 and CLI/server 79/79 passed.
- Targeted tests: Package signing/reproducibility 8/8 passed.
- Release gate: Passed before the isolated sample/root-stability change; final full build/test passed afterward.
- Manual checks: 18 PowerShell scripts parsed, Bash syntax check exited 0, git diff --check passed, required release files present, stale-scope scan clean.

### Remaining
- Credentialed Windows service install/status/uninstall smoke check.
- Owner history/license/manual sign-off before any release publication.

### Handoff
- Exact next action: Owner reviews the release-readiness report and performs the credentialed service lifecycle check.
- Important context: The worktree is intentionally uncommitted; no tag, push, or publication was performed.
- Files to inspect first: `docs/development/RELEASE_READINESS.md`, `scripts/windows/install-ows-agent-service.ps1`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Reviewer workflow and inspect hardening

### Completed
- Audited the roadmap against the actual CLI and found that documented reviewer commands could not consume an explicit package path.
- Added optional positional package paths to `ows verify`, `ows inspect`, and `ows report` while preserving current-directory defaults.
- Expanded `ows inspect` with manifest, artifact path/size/hash metadata, timeline events, inferred activity periods, and structured archive errors.
- Corrected active architecture/package/CLI documentation contradictions and added root entry points for architecture, package format, and threat model docs.

### Changed
- Added: `ARCHITECTURE.md`, `PACKAGE_FORMAT.md`, `THREAT_MODEL.md`, and `tests/Ows.Cli.Tests/ReviewerPackageArgumentTests.cs`.
- Modified: reviewer CLI builders, reviewer/documentation pages, and continuity notes.
- Deleted: None.

### Validation
- Build: 0 warnings and 0 errors.
- Targeted tests: reviewer package-path/inspect tests 2/2 passed.
- Full tests: Core 130/130 and CLI/server 80/80 passed.
- Release gate: Passed; live pilot trust Verified, reviewer denial 403, raw-key leak false, verifier stopped.
- Manual checks: malformed package inspection returned structured JSON and exit code 1; OwsAgent service remains uninstalled and untouched.

### Remaining
- Owner credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Owner follows the three service commands in `docs/development/RELEASE_READINESS.md` under the intended Windows user account.
- Important context: The reviewer commands now accept `<package.owspkg>` from any working directory; reports are written beside the selected package.
- Files to inspect first: `src/Ows.Cli/Commands/InspectCommandBuilder.cs`, `src/Ows.Cli/Commands/VerifyCommandBuilder.cs`, `src/Ows.Cli/Commands/ReportCommandBuilder.cs`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Service installer lifecycle hardening

### Completed
- Changed the Windows installer to start and wait for the Agent service after registration.
- Added rollback of the service registration when startup fails, avoiding a half-installed service.
- Documented the exact owner smoke-check commands.

### Changed
- Added: no new files.
- Modified: `scripts/windows/install-ows-agent-service.ps1`, CLI service documentation, and release-readiness instructions.
- Deleted: None.

### Validation
- Build/test: Existing validated tree remains green: Core 130/130 and CLI/server 79/79.
- Manual checks: PowerShell syntax passed for 18 scripts; OwsAgent absence confirmed on this machine; no service mutation attempted.

### Remaining
- Owner must execute credentialed install/status/uninstall against the intended Windows account.

### Handoff
- Exact next action: Run the three commands in `docs/development/RELEASE_READINESS.md` from an initialized project/account context.
- Important context: The installer now fails cleanly if SCM startup fails.
- Files to inspect first: `scripts/windows/install-ows-agent-service.ps1`, `scripts/windows/get-ows-agent-service-status.ps1`, and `scripts/windows/uninstall-ows-agent-service.ps1`.

## 2026-07-13 — Integrity hardening and honest release gate

### Completed
- Added receipt-chain byte-hash validation with a narrow trusted-remote exception for packages intentionally lacking packaged receipts.
- Made malformed ZIP input return an explicit Invalid verification result instead of escaping as an exception.
- Added Windows current-user DPAPI protection for newly created local signing keys; Unix keys remain user-mode restricted and legacy key files still load.
- Hid legacy session/watch/event and remote package subcommands from default help while preserving pilot compatibility.
- Fixed the live pilot gate to wait for delayed file events and fail unless completed package trust is Verified.

### Changed
- Added: ProtectedData dependency and integrity/lifecycle regression coverage.
- Modified: package verifier, artifact hash verifier, signing key store, CLI visibility, pilot timing gate, release-readiness notes.
- Deleted: None.

### Validation
- Build: 0 warnings and 0 errors.
- Full tests: Core 130/130 and CLI/server 79/79 passed.
- Targeted tests: package signing/integrity 11/11 passed.
- Release gate: Passed; restore/build/tests/Compose/live pilot all passed and live PilotTrust=Verified.
- Manual checks: PowerShell syntax passed for 18 scripts; git diff --check passed; Docker and verifier ports clear after cleanup.

### Remaining
- Credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/manual sign-off before publication.

### Handoff
- Exact next action: Owner reviews the release-readiness report and performs the service lifecycle check under the intended user account.
- Important context: Earlier gate output was false-green on trust; the current gate asserts and records Verified trust.
- Files to inspect first: `scripts/windows/install-ows-agent-service.ps1`, `scripts/windows/get-ows-agent-service-status.ps1`, `scripts/windows/uninstall-ows-agent-service.ps1`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Final verified release-gate rerun

### Completed
- Reran the release gate after integrity, DPAPI, CLI visibility, and pilot timing fixes.
- Confirmed the live pilot package completed with trust status Verified rather than merely Completed.
- Stopped PostgreSQL and confirmed verifier ports were clear afterward.

### Changed
- Added: final release-gate evidence and continuity handoff.
- Modified: None beyond the validated tree.
- Deleted: None.

### Validation
- Build/test: Gate restore, build, and full test steps passed.
- Full tests: Core 130/130 and CLI/server 79/79.
- Live pilot: Healthy/Ready, package Completed, PilotTrust Verified, reviewer denial 403, no raw key leak.
- Cleanup: Docker container stopped; ports 5078 and 5432 clear.

### Remaining
- Credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/manual sign-off before publication.

### Handoff
- Exact next action: Owner runs the three Windows service scripts with the intended user account and reviews RELEASE_READINESS.md.
- Important context: No tag, push, commit, or publication was performed.
- Files to inspect first: `docs/development/RELEASE_READINESS.md`, `scripts/windows/install-ows-agent-service.ps1`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Continuity reconciliation and final validation

### Completed
- Reconciled the continuity notes with the repository state and confirmed the onboarding documentation patch is present.
- Kept the release candidate explicitly owner-gated for credentialed Windows service lifecycle validation and manual sign-off.

### Changed
- Added: no product code; continuity evidence for the final validation pass.
- Modified: `.agent/CURRENT_TASK.md`.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Previously passed package-signing/integrity suite 11/11.
- Full tests: Core 130/130 and CLI/server 79/79 passed.
- Manual checks: PowerShell syntax passed for 18 scripts; repository remains uncommitted by design; no Windows service mutation attempted.

### Remaining
- Owner credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Owner follows the three service commands in `docs/development/RELEASE_READINESS.md` under the intended Windows user account.
- Important context: Do not install as LocalSystem; the Agent must read the same user-local registry and project files used by `ows init`.
- Files to inspect first: `docs/development/RELEASE_READINESS.md`, `scripts/windows/install-ows-agent-service.ps1`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Release-gate dependency cleanup

### Completed
- Found that the successful Windows release gate left the workspace PostgreSQL container listening on port 5432.
- Stopped the exact `ows-postgres` Compose stack and added exit cleanup to Windows and Unix release gates.

### Changed
- Added: release-gate Compose teardown in both platform scripts.
- Modified: `scripts/windows/run-release-regression-gate.ps1`, `scripts/unix/run-release-regression-gate.sh`.
- Deleted: None.

### Validation
- Build: Gate rerun passed with 0 warnings and 0 errors.
- Full tests: Core 130/130 and CLI/server 80/80 passed during the gate.
- Release gate: Live pilot trust Verified, reviewer denial 403, raw-key leak false.
- Manual checks: PowerShell and Bash gate syntax passed; ports 5078/5432 clear after cleanup; OwsAgent service remains uninstalled.

### Remaining
- Owner credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Owner follows the three service commands in `docs/development/RELEASE_READINESS.md` under the intended Windows user account.
- Important context: The release gate now tears down its local PostgreSQL dependency automatically.
- Files to inspect first: `scripts/windows/run-release-regression-gate.ps1`, `scripts/unix/run-release-regression-gate.sh`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Final student-path wording audit

### Completed
- Clarified that Windows Agent service installation is a machine-owner/administrator setup step, not part of the student ceremony.
- Recorded the unimplemented timeline retention boundary honestly rather than adding lossy truncation to the v0.1 chain.

### Changed
- Added: no product code.
- Modified: `GETTING_STARTED.md`, `docs/workflows/STUDENT_WORKFLOW.md`, and continuity notes.
- Deleted: None.

### Validation
- Build: Not rerun; documentation and continuity-only change after the passing release gate.
- Targeted tests: Not applicable.
- Full tests: Last gate passed Core 130/130 and CLI/server 80/80.
- Manual checks: OwsAgent service remains uninstalled; ports 5078/5432 remain clear.

### Remaining
- Owner credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Owner reviews `docs/development/RELEASE_READINESS.md` and executes the service lifecycle commands with the intended account.
- Important context: The student path remains `ows init` followed by `ows package`; service setup is host-owned.
- Files to inspect first: `GETTING_STARTED.md`, `docs/workflows/STUDENT_WORKFLOW.md`, `.agent/NEXT_STEPS.md`, and `.agent/DECISIONS.md`.

## 2026-07-13 — Release-readiness evidence reconciliation

### Completed
- Corrected the release-readiness report from CLI/server 79/79 to the verified 80/80 total.
- Added explicit supported-platform, package/API stability, and remaining-future-work sections required by the release audit.

### Changed
- Added: no product code.
- Modified: `docs/development/RELEASE_READINESS.md` and continuity notes.
- Deleted: None.

### Validation
- Build: Not rerun; documentation-only change after the passing gate.
- Targeted tests: Not applicable.
- Full tests: Last gate passed Core 130/130 and CLI/server 80/80.
- Manual checks: Release gate remains `Passed` with pilot trust `Verified`; ports 5078/5432 clear; OwsAgent service uninstalled.

### Remaining
- Owner credentialed Windows Agent service install/status/uninstall smoke check.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Owner reviews the corrected release-readiness report and runs the Windows service lifecycle commands.
- Important context: No tag, push, publication, or service mutation has been performed.
- Files to inspect first: `docs/development/RELEASE_READINESS.md`, `.agent/CURRENT_TASK.md`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Release hygiene path scan

### Completed
- Found and normalized all absolute developer paths in the tracked historical trust-boundary plan.
- Re-scanned the current checkout: no absolute Windows-user or macOS-user paths remain outside ignored/generated state.
- Classified the remaining `ows-dev` and `admin` strings as documented synthetic local-development defaults, not personal credentials.

### Changed
- Added: no product code.
- Modified: `docs/superpowers/plans/2026-06-19-remote-trust-boundary-foundation.md` and continuity notes.
- Deleted: None.

### Validation
- Build: Not rerun; documentation-only change after the passing gate.
- Targeted tests: Not applicable.
- Full tests: Last gate passed Core 130/130 and CLI/server 80/80.
- Manual checks: Current path scan clean; OwsAgent service uninstalled; ports 5078/5432 clear.

### Remaining
- Owner reviews visible git history for legacy machine-specific planning text, license, and generated artifacts before publication.
- Owner credentialed Windows Agent service install/status/uninstall smoke check and final release sign-off.

### Handoff
- Exact next action: Owner reviews the current worktree/history and executes the Windows service lifecycle commands.
- Important context: The current checkout is sanitized; rewriting historical commits was not authorized or necessary for the working-tree release audit.
- Files to inspect first: `docs/superpowers/plans/2026-06-19-remote-trust-boundary-foundation.md`, `docs/development/RELEASE_READINESS.md`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Owner-gated handoff verification

### Completed
- Rechecked the final release evidence and external runtime state after the hygiene audit.
- Confirmed the release gate is `Passed`, the live package trust status is `Verified`, and the verifier ports are clear.

### Changed
- Added: no product code.
- Modified: continuity handoff only.
- Deleted: None.

### Validation
- Build: Last release gate build passed with 0 warnings and 0 errors.
- Targeted tests: Last release gate passed package/integrity and reviewer-path coverage.
- Full tests: Core 130/130 and CLI/server 80/80 passed.
- Manual checks: `OwsAgent` is still not installed; credentialed SCM lifecycle cannot be truthfully claimed without the intended account.

### Remaining
- Owner must install, status-check, and uninstall the Windows Agent service using the account that owns the user-local registry and project files.
- Owner must complete history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Run the three commands in `docs/development/RELEASE_READINESS.md` with the intended Windows account.
- Important context: This is an external credential/SCM dependency, not a failing repository check; no service mutation was attempted.
- Files to inspect first: `scripts/windows/install-ows-agent-service.ps1`, `scripts/windows/get-ows-agent-service-status.ps1`, `scripts/windows/uninstall-ows-agent-service.ps1`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Passwordless per-user Agent bootstrap

### Completed
- Replaced the credentialed SCM install as the normal Windows bootstrap with a current-user Scheduled Task.
- Fixed the Windows PowerShell task principal enum and verified the lifecycle under the active account.

### Changed
- Added: `scripts/windows/install-ows-agent.ps1`, `get-ows-agent-status.ps1`, and `uninstall-ows-agent.ps1`.
- Modified: release-readiness instructions, changelog, and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors; Agent bootstrap publish succeeded.
- Targeted tests: Install, Running status, uninstall, and NotInstalled status smoke check passed without credentials.
- Full tests: `dotnet test OWS.sln -nologo --no-build --no-restore` passed 210/210.
- Manual checks: No OwsAgent Scheduled Task remains after cleanup; project metadata and user-local registry were preserved.

### Remaining
- Owner history/license/generated-artifact review and final release sign-off.
- Optional cleanup: remove the stale `OwsAgent` task from Task Scheduler as Administrator; it is `Ready` and not running.
- Optional SCM service lifecycle remains managed-deployment work and is not required for the normal student workflow.

### Handoff
- Exact next action: Owner reviews the release-readiness evidence and current worktree scope.
- Important context: The normal Windows bootstrap requires no password; it runs only in the current user's logon session and watches initialized projects.
- Files to inspect first: `scripts/windows/install-ows-agent.ps1`, `docs/development/RELEASE_READINESS.md`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Hidden Agent task refinement

### Completed
- Confirmed the normal Agent can run as a hidden per-user Scheduled Task without a visible command window.
- Avoided the stale inaccessible `OwsAgent` task from the earlier setup by using the user-owned default task name `OwsAgent.User`.

### Changed
- Modified: Windows bootstrap/status/uninstall scripts and current-task continuity notes.
- Added: no new files.
- Deleted: None.

### Validation
- Build: Release publish succeeded during bootstrap install.
- Targeted tests: `OwsAgent.User` installed, reported `Running`, was hidden, and uninstalled without credentials.
- Full tests: Previous final run remains 210/210; script syntax remains 21/21.
- Manual checks: The per-user task was removed after validation; the older inaccessible `OwsAgent` entry is not running.

### Remaining
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Run `scripts/windows/install-ows-agent.ps1` when you want the Agent enabled.
- Important context: This is a hidden per-user task, not a Windows service and not a visible command window.
- Files to inspect first: `scripts/windows/install-ows-agent.ps1`, `scripts/windows/get-ows-agent-status.ps1`, and `scripts/windows/uninstall-ows-agent.ps1`.

## 2026-07-13 — Windows Setup.exe SCM service path

### Completed
- Replaced the visible/passwordless Scheduled Task product path with a self-contained `Ows.Setup.exe` that installs and hosts the SCM service.
- Moved the Windows Agent registry to `%ProgramData%\OpenWorkStandard` so LocalSystem and the CLI share explicit project registrations.
- Added automatic cleanup of legacy `OwsAgent` and `OwsAgent.User` Scheduled Tasks during setup install/uninstall.

### Changed
- Added: `src/Ows.Setup`, `scripts/windows/build-ows-setup.ps1`, and the SCM setup/security documentation.
- Modified: Windows registry default, local IPC pipe compatibility, release/startup/privacy/security docs, and durable decisions.
- Deleted: legacy Scheduled Task and credentialed service installer scripts.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: setup publish produced a 75,616,772-byte self-contained `Ows.Setup.exe`; PE subsystem 2 confirms no console subsystem.
- Full tests: `dotnet test OWS.sln -nologo --no-build --no-restore` passed 210/210.
- Manual checks: PowerShell syntax passed 16/16; `git diff --check` passed. Actual SCM install remains UAC-gated and requires owner execution.

### Remaining
- Run the published setup executable with UAC approval and verify Services.msc start/stop/uninstall behavior on this machine.
- Owner history/license/generated-artifact review and final release sign-off.

### Handoff
- Exact next action: Double-click `artifacts/ows-setup/Ows.Setup.exe`, then verify `OWS Agent` in Services.msc.
- Important context: The service runs as LocalSystem without a service-account password; uninstall preserves project `.ows` data unless `--purge-data` is supplied.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `scripts/windows/build-ows-setup.ps1`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Setup failure surfaced and fixed

### Completed
- Diagnosed the disappearing setup process through Windows Application/.NET Runtime events.
- Fixed malformed `sc.exe create` quoting by passing service arguments through `ProcessStartInfo.ArgumentList`.
- Added an error MessageBox so future setup failures are visible instead of silently terminating.

### Changed
- Added: no new files.
- Modified: `src/Ows.Setup/Program.cs` and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Setup republished as a self-contained GUI executable.
- Full tests: 210/210 passed.
- Manual checks: Event log confirmed the original failure was `sc.exe` usage output; updated setup is published at `artifacts/ows-setup/Ows.Setup.exe`.

## 2026-07-13 — Live SCM and uninstall registration verification

### Completed
- Confirmed the installed service and Windows uninstall registration on the live machine.

### Changed
- Added: no new files.
- Modified: continuity handoff only.
- Deleted: None.

### Validation
- Build: Current setup build passes with 0 warnings and 0 errors.
- Targeted tests: `OWS Agent` is Running, Automatic, LocalSystem, and points to `Ows.Setup.exe --service`.
- Full tests: 210/210 passed.
- Manual checks: Service process has no main window; Installed apps metadata exists with an uninstall command. Service restart was not attempted because the current shell is not elevated.

### Remaining
- Confirm the Add/Remove Programs uninstall action removes the service, install directory, and uninstall registry entry; preserve shared data unless purge is explicit.

### Handoff
- Exact next action: Use `Open Work Standard` from Windows Installed apps to verify uninstall, then reinstall from `artifacts/ows-setup/Ows.Setup.exe` if the Agent should remain active.
- Important context: The live service is currently installed and running correctly; stopping it requires Administrator rights.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `C:\Program Files\Open Work Standard\Ows.Setup.exe`, and the uninstall registry key.

### Remaining
- Rerun the updated setup with UAC approval and verify `OWS Agent` in Services.msc.

### Handoff
- Exact next action: Double-click the newly published `artifacts/ows-setup/Ows.Setup.exe` and approve UAC.
- Important context: The prior copied payload under Program Files is not a registered service; the updated setup replaces it and removes legacy Scheduled Tasks.
- Files to inspect first: `src/Ows.Setup/Program.cs` and `artifacts/ows-setup/Ows.Setup.exe` (historical handoff).

## 2026-07-13 — Installer lifecycle hardening

### Completed
- Fixed shared-registry contention between the LocalSystem service poller and CLI registration with a bounded cross-process lock retry.
- Made service stop/delete wait for `STOPPED` before cleanup and added an uninstall prompt for shared Agent data.
- Corrected user-facing Windows setup and uninstall documentation.

### Changed
- Added: explicit uninstall data-choice behavior and registry lock retry.
- Modified: `src/Ows.Setup/Program.cs`, `src/Ows.Core/Agent/OwsProjectRegistry.cs`, README/docs, and continuity decisions.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: installer publish succeeded; package command regression passed 1/1 with the live service running.
- Full tests: Core 130/130 and CLI/server 80/80 passed.
- Manual checks: live `OwsAgent` remains Running/Automatic/LocalSystem with no window; uninstall registry entry remains present; destructive UAC uninstall not run.

### Remaining
- Run the Installed apps uninstall action with UAC and confirm service, Program Files payload, and uninstall registry cleanup.

### Handoff
- Exact next action: Use `Open Work Standard` in Windows Installed apps and choose the desired shared-data option.
- Important context: The currently installed service predates the latest republished lock-retry binary; reinstall from the new artifact after lifecycle testing if needed.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `src/Ows.Core/Agent/OwsProjectRegistry.cs`, and `artifacts/ows-setup/Ows.Setup.exe`.

## 2026-07-13 — Stale visible task diagnosis

### Completed
- Traced the visible dotnet window to the old `OwsAgent` task, not the hidden `OwsAgent.User` bootstrap.
- Confirmed the old task is `Hidden=False`, while the new task is `Hidden=True`.

### Changed
- Added: incident and cleanup context to continuity notes.
- Modified: None.
- Deleted: None.

### Validation
- Build: Not rerun; no code change.
- Targeted tests: Task metadata inspection identified the source of the visible window.
- Full tests: Previous final run remains 210/210.
- Manual checks: No Agent process remains; the old task is `Ready` and requires Administrator rights to delete.

### Remaining
- Run `Unregister-ScheduledTask -TaskName OwsAgent -Confirm:$false` once from an Administrator PowerShell if the stale task should be removed.

### Handoff
- Exact next action: Remove the stale visible task, then use `scripts/windows/install-ows-agent.ps1` for the hidden `OwsAgent.User` bootstrap.
- Important context: No Windows service or account password is required for the normal path.
- Files to inspect first: `scripts/windows/install-ows-agent.ps1` and `scripts/windows/uninstall-ows-agent.ps1`.

## 2026-07-13 — SCM setup handoff correction

### Completed
- Confirmed the requested product path is the self-contained `Ows.Setup.exe` SCM service, not a Scheduled Task.

### Changed
- Added: no new product files.
- Modified: continuity handoff ordering.
- Deleted: None.

### Validation
- Build: 0 warnings and 0 errors.
- Targeted tests: `Ows.Setup.exe` is self-contained and GUI-subsystem; service installation is UAC-gated.
- Full tests: 210/210 passed.
- Manual checks: 16 PowerShell scripts parsed; diff check passed.

### Remaining
- Owner must run the setup executable with UAC approval and verify the service in Services.msc.

### Handoff
- Exact next action: Double-click `artifacts/ows-setup/Ows.Setup.exe`.
- Important context: Setup removes legacy Scheduled Tasks, installs `OWS Agent` as LocalSystem, and supports `--uninstall` plus explicit `--purge-data`.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `scripts/windows/build-ows-setup.ps1`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Setup failure surfaced and fixed

### Completed
- Diagnosed the disappearing setup process through Windows Application/.NET Runtime events.
- Fixed malformed `sc.exe create` quoting by passing service arguments through `ProcessStartInfo.ArgumentList`.
- Added an error MessageBox so future setup failures are visible instead of silently terminating.

### Changed
- Added: no new files.
- Modified: `src/Ows.Setup/Program.cs` and continuity notes.
- Deleted: None.

### Validation
- Build: `dotnet build OWS.sln -nologo` passed with 0 warnings and 0 errors.
- Targeted tests: Setup republished as a self-contained GUI executable.
- Full tests: 210/210 passed.
- Manual checks: Event log confirmed the original failure was `sc.exe` usage output; updated setup is published at `artifacts/ows-setup/Ows.Setup.exe`.

### Remaining
- Rerun the updated setup with UAC approval and verify `OWS Agent` in Services.msc.

### Handoff
- Exact next action: Double-click the newly published `artifacts/ows-setup/Ows.Setup.exe` and approve UAC.
- Important context: The prior copied payload under Program Files is not a registered service; the updated setup replaces it and removes legacy Scheduled Tasks.
- Files to inspect first: `src/Ows.Setup/Program.cs` and `artifacts/ows-setup/Ows.Setup.exe`.

## 2026-07-13 — Safe SCM cleanup guard

### Completed
- Changed uninstall/reinstall to refuse service deletion when SCM does not report `STOPPED` within 10 seconds.
- Republished the self-contained setup artifact after the final installer change.

### Changed
- Added: no new files.
- Modified: `src/Ows.Setup/Program.cs` and continuity notes.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish succeeded.
- Full tests: Core 130/130 and CLI/server 80/80 passed.
- Manual checks: live service remains Running/Automatic/LocalSystem, no legacy Agent Scheduled Tasks were found, and `git diff --check` passed.

### Remaining
- Destructive Installed apps uninstall/reinstall still requires a UAC-approved elevated run.

### Handoff
- Exact next action: Run `Open Work Standard` from Windows Installed apps and verify the post-uninstall filesystem and registry state.
- Important context: The repository artifact is current; the installed Program Files service remains the prior live installation until reinstall.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `src/Ows.Core/Agent/OwsProjectRegistry.cs`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Registry contention regression coverage

### Completed
- Added a focused test proving registry writes retry while the lock is held.
- Reconciled the release evidence totals with the new test.

### Changed
- Added: `Registry_ShouldRetryWhenTheRegistryLockIsHeld` in `tests/Ows.Core.Tests/OwsAgentHostTests.cs`.
- Modified: continuity notes.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Registry contention test passed 1/1.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: Live service and uninstall registration remain present; destructive UAC uninstall remains unrun.

### Remaining
- Run the Installed apps uninstall/reinstall smoke test with UAC approval.

### Handoff
- Exact next action: Use `Open Work Standard` in Windows Installed apps, then reinstall from the current setup artifact if the service should remain active.
- Important context: The registry contention fix is now covered by a Core regression test; project `.ows` folders must not be deleted.
- Files to inspect first: `tests/Ows.Core.Tests/OwsAgentHostTests.cs`, `src/Ows.Core/Agent/OwsProjectRegistry.cs`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Commit current release candidate

### Completed
- Prepared the current OWS release-candidate worktree for the first authorized commit.
- Confirmed the live SCM service and registry state remain healthy.

### Changed
- Added: no product files in this work unit.
- Modified: continuity notes only.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Registry contention test passed 1/1.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: `OwsAgent` Running/Automatic/LocalSystem; no legacy Agent Scheduled Tasks; uninstall entry present; `git diff --check` passed.

### Remaining
- Reconcile the stale reviewer/management wording scan.
- Run the destructive Installed apps uninstall/reinstall smoke test with UAC approval.

### Handoff
- Exact next action: Stage and commit the current worktree, then continue the release audit.
- Important context: The user explicitly authorized committing all current uncommitted work; do not push or publish without separate authorization.
- Files to inspect first: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Reviewer-surface terminology cleanup

### Completed
- Replaced stale professor-facing wording with reviewer-facing language in the active docs and report comment.
- Removed the obsolete enrollment roster-read checklist item.
- Recorded the clean baseline commit in the current-task notes.

### Changed
- Added: no new files.
- Modified: `docs/development/PROJECT_STATUS.md`, `docs/development/ROADMAP_CHECKLIST.md`, `docs/operations/TROUBLESHOOTING.md`, `docs/workflows/PILOT_DEMO.md`, `src/Ows.Core/Reporting/OwsReportGenerator.cs`, and continuity notes.
- Deleted: None.

### Validation
- Build: Not rerun; documentation/comment-only follow-up after the passing 0-warning build.
- Targeted tests: Not applicable.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: No active deleted bootstrap-script references remain outside intentional legacy-task cleanup.

### Remaining
- Run the destructive Installed apps uninstall/reinstall smoke test with UAC approval.

### Handoff
- Exact next action: Run `git diff --check`, then commit this documentation follow-up.
- Important context: Do not push or publish; the current baseline is `2ef1195`.
- Files to inspect first: `.agent/CURRENT_TASK.md`, `docs/development/ROADMAP_CHECKLIST.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Post-commit handoff refresh

### Completed
- Recorded the documentation follow-up commit and reconciled the live repository state.

### Changed
- Added: no product files.
- Modified: `.agent/CURRENT_TASK.md` and `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Not rerun; continuity-only change after the passing build.
- Targeted tests: Not applicable.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: Working tree clean at `8412adc`; live service remains Running/Automatic/LocalSystem.

### Remaining
- Run the destructive Installed apps uninstall/reinstall smoke test with UAC approval.

### Handoff
- Exact next action: Use Windows Installed apps to uninstall, verify cleanup, then reinstall from the current setup artifact if needed.
- Important context: Do not push or publish; project `.ows` folders must remain untouched.
- Files to inspect first: `.agent/NEXT_STEPS.md`, `docs/development/RELEASE_READINESS.md`, and `src/Ows.Setup/Program.cs`.

## 2026-07-13 — Installed payload freshness audit

### Completed
- Compared the live Program Files service binary with the current self-contained setup artifact.
- Confirmed the service and uninstall registration are healthy but the installed payload predates the current artifact.

### Changed
- Added: no product files.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, and `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Not rerun; state-audit only after the passing build.
- Targeted tests: Not applicable.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: Installed hash `602C2510DF807A5A20012091BAADFA43D3E2732003594CD19D022EB5AC33EC02`; artifact hash `B78291E88F1143A4805EE0867E43EEEFB480006AA9BF410853F43678100CF116`; service Running/Automatic/LocalSystem.

### Remaining
- UAC reinstall of the current artifact, followed by Add/Remove Programs uninstall cleanup.

### Handoff
- Exact next action: Double-click `artifacts\ows-setup\Ows.Setup.exe`, approve UAC, and recheck the service path/hash.
- Important context: Do not claim the current installer code is live until the hashes match; project `.ows` folders must remain untouched.
- Files to inspect first: `.agent/NEXT_STEPS.md`, `src/Ows.Setup/Program.cs`, and `artifacts/ows-setup/Ows.Setup.exe`.

## 2026-07-13 — Automated owner-review audit

### Completed
- Verified the MIT license is tracked and present.
- Confirmed no tracked generated directories, binaries, archives, or private-key files are in the release baseline.
- Confirmed the worktree is clean and recent commit history is inspectable.

### Changed
- Added: no product files.
- Modified: `.agent/CURRENT_TASK.md` and `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Not rerun; owner-review audit only after the passing build.
- Targeted tests: Not applicable.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: `LICENSE` present; tracked generated/binary/private-key scans clean; `git diff --check` passed.

### Remaining
- UAC reinstall/uninstall lifecycle test and human release sign-off.

### Handoff
- Exact next action: Owner approves UAC reinstall from the current setup artifact, then completes the Installed apps cleanup check.
- Important context: Automated hygiene is evidence, not a substitute for owner history/license/sign-off review.
- Files to inspect first: `LICENSE`, `.agent/NEXT_STEPS.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — SCM failure recovery

### Completed
- Configured native SCM restart actions for unexpected Agent exits at 5, 30, and 60 seconds.
- Updated the Windows setup, CLI, release-readiness, and Agent design documentation.
- Republished the self-contained setup artifact.

### Changed
- Added: SCM failure recovery configuration in `src/Ows.Setup/Program.cs`.
- Modified: `AGENT_DESIGN.md`, `docs/development/CLI.md`, `docs/development/RELEASE_READINESS.md`, and continuity notes.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish succeeded.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: Current artifact hash is `6A07E05F0699291CE310B767C8CD3DC0213150EFF76A377EBD959865C8E36D99`; installed service remains older and UAC reinstall is pending.

### Remaining
- Reinstall the current artifact with UAC and verify SCM recovery settings on the live service.
- Run the Installed apps uninstall cleanup and complete human release sign-off.

### Handoff
- Exact next action: Double-click `artifacts\ows-setup\Ows.Setup.exe`, approve UAC, then inspect `sc.exe qfailure OwsAgent`.
- Important context: The current installer configures restart actions; the currently running service does not contain this latest configuration until reinstall.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `artifacts/ows-setup/Ows.Setup.exe`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — SCM recovery syntax validation

### Completed
- Validated the installer’s `sc.exe failure` argument shape against the live service.
- Confirmed the command parses and reaches service authorization; the non-admin shell receives expected access denied.
- Queried the live service and confirmed `RESET_PERIOD: 0`, proving recovery actions remain unapplied until UAC reinstall.

### Changed
- Added: no product files.
- Modified: `.agent/CURRENT_TASK.md` and `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Not rerun; native command validation only.
- Targeted tests: Not applicable.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: `sc.exe failure` syntax accepted; live service remains Running/Automatic/LocalSystem with no recovery actions.

### Remaining
- UAC reinstall current artifact, verify `sc.exe qfailure OwsAgent`, then test uninstall cleanup.

### Handoff
- Exact next action: Run the current `artifacts\ows-setup\Ows.Setup.exe` as Administrator.
- Important context: Expected post-install recovery output should show three restart actions at 5000, 30000, and 60000 milliseconds.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `.agent/NEXT_STEPS.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — UAC-gated lifecycle handoff

### Completed
- Exhausted safe non-administrator checks for the installer lifecycle.
- Confirmed the current service is healthy but still runs the older payload without recovery actions.
- Preserved a precise owner handoff for the one remaining external validation path.

### Changed
- Added: no product files.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, and `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Last build passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish and native `sc.exe` argument parsing validated.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: Service Running/Automatic/LocalSystem; `RESET_PERIOD: 0`; current artifact and installed payload hashes differ; worktree clean.

### Remaining
- Owner-approved UAC reinstall, recovery-policy verification, Installed apps uninstall cleanup, and human release sign-off.

### Handoff
- Exact next action: Owner runs the current setup artifact as Administrator, then checks `sc.exe qfailure OwsAgent` and the Installed apps uninstall path.
- Important context: This goal is blocked on that external elevation; no further safe repository-side change can prove the live lifecycle.
- Files to inspect first: `.agent/NEXT_STEPS.md`, `src/Ows.Setup/Program.cs`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Setup shutdown timeout correction

### Completed
- Diagnosed the owner smoke-test failure: setup reached SCM shutdown, but the fixed 10-second wait expired before the service reported `STOPPED`.
- Made stopped-service handling immediate, surfaced non-1062 stop failures, and extended the bounded stop wait to 30 seconds.

### Changed
- Added: None.
- Modified: `src/Ows.Setup/Program.cs`, `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Not applicable.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: Existing elevated setup error dialog still locks the old artifact; service is stopped and installed files remain in place.

### Remaining
- Close the elevated setup error dialog, republish the corrected setup, and rerun the UAC install smoke test.
- Run the Installed apps uninstall cleanup and complete human release sign-off.

### Handoff
- Exact next action: Click OK on the open `Open Work Standard Setup Error` dialog, then run `.\scripts\windows\build-ows-setup.ps1`.
- Important context: The source fix is complete and tested; the current artifact has not yet been regenerated because PID 24668 holds it open with elevation.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `.agent/CURRENT_TASK.md`, and `artifacts/ows-setup/Ows.Setup.exe`.

## 2026-07-13 — Corrected setup artifact republished

### Completed
- Closed the elevated setup error dialog that was locking the previous artifact.
- Republished the corrected self-contained setup executable.

### Changed
- Added: Refreshed `artifacts/ows-setup/Ows.Setup.exe` build output.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Setup publish succeeded.
- Targeted tests: Source build passed with 0 warnings and 0 errors.
- Full tests: Core 131/131 and CLI/server 80/80 passed before publish.
- Manual checks: New setup SHA-256 is `7D87D4EB4EF3198FF5367984B545BCC53970EB8731E193FE038D16B7E9CB85AD`; service is stopped and old installed payload remains preserved.

### Remaining
- Run the corrected setup with UAC approval and verify service start/recovery settings.
- Run the Installed apps uninstall cleanup and complete human release sign-off.

### Handoff
- Exact next action: Double-click `artifacts\ows-setup\Ows.Setup.exe`, approve UAC, then inspect Services and `sc.exe qfailure OwsAgent`.
- Important context: The corrected installer waits up to 30 seconds for normal SCM shutdown and reports access-denied stop failures directly.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `artifacts/ows-setup/Ows.Setup.exe`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Setup stop result hardening

### Completed
- Accepted SCM exit code 1062 directly for the race where a service becomes stopped between query and stop.

### Changed
- Added: None.
- Modified: `src/Ows.Setup/Program.cs`, `.agent/CURRENT_TASK.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish succeeded.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: Final setup SHA-256 is `D5C08EF73902B396CDCA56DDB3D1088347D88087D9B97411374A9EA33302E4EB`.

### Remaining
- Owner must run the final setup artifact with UAC approval, then verify service and uninstall behavior.

### Handoff
- Exact next action: Double-click `artifacts\ows-setup\Ows.Setup.exe`, approve UAC, and confirm the Agent appears Running in Services.
- Important context: Current installed payload remains the older hash until that UAC-approved reinstall.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `artifacts/ows-setup/Ows.Setup.exe`, and `.agent/NEXT_STEPS.md`.

## 2026-07-13 — Live SCM install validation

### Completed
- Owner installed the corrected setup artifact with UAC approval.
- Confirmed `OwsAgent` is Running.
- Confirmed SCM recovery is configured for restart after 5, 30, and 60 seconds.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Previously passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish succeeded.
- Full tests: Previously passed Core 131/131 and CLI/server 80/80.
- Manual checks: `Get-Service OwsAgent` returned Running; `sc.exe qfailure OwsAgent` returned the expected recovery policy.

### Remaining
- Run Installed apps uninstall and verify service, payload, uninstall entry, and project-boundary preservation.

### Handoff
- Exact next action: Open Settings → Apps → Installed apps → Open Work Standard → Uninstall.
- Important context: The service install and recovery configuration are now owner-validated; choose shared-data deletion only if intended.
- Files to inspect first: `src/Ows.Setup/Program.cs`, `.agent/NEXT_STEPS.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Windows uninstall validation

### Completed
- Owner removed Open Work Standard through Windows Installed apps.
- Confirmed the OWS Agent service, Program Files payload, and uninstall registry entry were removed.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: None.

### Validation
- Build: Previously passed with 0 warnings and 0 errors.
- Targeted tests: Setup publish succeeded.
- Full tests: Previously passed Core 131/131 and CLI/server 80/80.
- Manual checks: `OwsAgent` absent; `C:\Program Files\Open Work Standard` absent; uninstall registry entry absent.

### Remaining
- Final owner review and explicit publication approval.

### Handoff
- Exact next action: Review release history, license, generated artifacts, and final worktree scope.
- Important context: The Windows install → service → recovery → uninstall lifecycle is now validated; no publication has been performed.
- Files to inspect first: `.agent/DECISIONS.md`, `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, and `docs/development/RELEASE_READINESS.md`.

## 2026-07-13 — Temporary repository output cleanup

### Completed
- Removed ignored generated build/test outputs, release artifacts, verifier caches, IDE metadata, and the local solution user-settings file.
- Removed the nested verifier cache found by the final dry-run.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/WORK_LOG.md`.
- Deleted: Ignored temporary files only; no tracked project files.

### Validation
- Build: Not rerun; cleanup did not change tracked source.
- Targeted tests: Not rerun.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: `git status --short` clean; `git clean -ndX` reports no remaining ignored files.

### Remaining
- Owner final review and explicit publication approval.

### Handoff
- Exact next action: Review the clean repository and release history before any publication decision.
- Important context: Local build outputs and the generated setup artifact were intentionally removed; rebuild with the existing scripts when needed.
- Files to inspect first: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.gitignore`, and `scripts/windows/build-ows-setup.ps1`.

## 2026-07-13 — Markdown documentation audit

### Completed
- Audited 67 Markdown files, their headings, and inbound Markdown links.
- Identified canonical docs, stable root aliases, remote-verifier operations material, archive snapshots, planning artifacts, and continuity files.
- Confirmed no product Markdown was deleted during this review.

### Changed
- Added: None.
- Modified: `.agent/CURRENT_TASK.md`, `.agent/NEXT_STEPS.md`, `.agent/WORK_LOG.md`.
- Deleted: Temporary `graphify-out/cache` generated by the failed semantic scan.

### Validation
- Build: Not rerun; audit-only.
- Targeted tests: Not rerun.
- Full tests: Last run passed Core 131/131 and CLI/server 80/80.
- Manual checks: Markdown reference scan completed; worktree is clean after removing graphify residue.

### Remaining
- Owner decision on the prune/merge set below.

### Handoff
- Exact next action: Approve the documentation reduction plan before any product-doc deletion.
- Important context: The local-first v0 core is documented separately from a much larger optional remote-verifier/operations surface; deletion should preserve stable root aliases and `.agent` continuity files.
- Files to inspect first: `README.md`, `docs/START_HERE.md`, `docs/core/`, `docs/development/PROJECT_STATUS.md`, `docs/development/RELEASE_CHECKLIST.md`, and `docs/archive/`.

### Proposed prune/merge set
- Keep: `README.md`, `GETTING_STARTED.md`, `docs/START_HERE.md`, core protocol/security docs, `docs/workflows/STUDENT_WORKFLOW.md`, `docs/workflows/REVIEW_GUIDANCE.md`, `docs/development/CLI.md`, `docs/development/ROADMAP_CHECKLIST.md`, contributor/security/legal files, GitHub templates, and `.agent/*`.
- Keep as stable aliases: root `SPEC.md`, `ARCHITECTURE.md`, `AGENT_DESIGN.md`, `CLI_REFERENCE.md`, `PACKAGE_FORMAT.md`, and `THREAT_MODEL.md`; they are tiny external-link-compatible entry points.
- Merge/trim: `docs/development/RELEASE_CHECKLIST.md` + `REGRESSION_GATE.md` + `RELEASE_READINESS.md` into one release checklist; merge `docs/development/VERIFIER_LOCAL_DEV.md` + `VERIFIER_STORAGE.md` if the remote verifier remains in scope.
- Remove or replace: `docs/development/PROJECT_STATUS.md`, which is a large mutable snapshot overlapping the roadmap, changelog, and release checklist and already contains stale status.
- Archive or delete: `docs/archive/*`, `docs/superpowers/plans/*`, and placeholder/future integration docs (`docs/integrations/DESKTOP_UI.md`, `docs/integrations/OIDC_INTEGRATION.md`, `docs/reference/SECURITY_CHANNELS.md`) unless the remote-verifier roadmap is intentionally retained.
- Scope decision: most `docs/operations/*`, `docs/workflows/LOCAL_DEMO.md`, and `docs/workflows/PILOT_DEMO.md` document the optional remote verifier/management layer; keep them only if that pilot remains an active v0 deliverable, otherwise move them to a separate future project or delete after preserving Git history.
- Optional: delete `samples/minimal-project` and remove its two documentation references; it is not required by build, runtime, installer, or tests.

## 2026-07-13 — Documentation prune and release-doc merge

### Completed
- Removed the optional `samples/minimal-project` fixture.
- Removed stale archive snapshots, the completed remote-trust implementation plan, and the mutable `PROJECT_STATUS.md` snapshot.
- Merged release scope and gate guidance into `docs/development/RELEASE_CHECKLIST.md`; removed duplicate `REGRESSION_GATE.md` and `RELEASE_READINESS.md`.
- Repaired README, Start Here, architecture, roadmap, and release references.

### Changed
- Added: Release scope and honest limitations to `docs/development/RELEASE_CHECKLIST.md`.
- Modified: `README.md`, `GETTING_STARTED.md`, `RELEASE.md`, `docs/START_HERE.md`, `docs/core/ARCHITECTURE.md`, `docs/development/ROADMAP_CHECKLIST.md`, `docs/development/RELEASE_CHECKLIST.md`.
- Deleted: `samples/minimal-project/*`, `docs/archive/*`, `docs/superpowers/plans/2026-06-19-remote-trust-boundary-foundation.md`, `docs/development/PROJECT_STATUS.md`, `docs/development/REGRESSION_GATE.md`, and `docs/development/RELEASE_READINESS.md`.

### Validation
- Build: Passed with 0 warnings and 0 errors.
- Targeted tests: Not applicable.
- Full tests: Core 131/131 and CLI/server 80/80 passed.
- Manual checks: Markdown reduced from 67 to 59 files; internal Markdown links clean; `git diff --check` clean.

### Remaining
- Final owner review and explicit publication approval.
- Optional future decision: split or remove the active remote-verifier operations documentation if that surface moves to a separate project.

### Handoff
- Exact next action: Review the reduced Markdown surface and release checklist.
- Important context: Active remote-verifier operations docs were intentionally preserved because they describe implemented optional code; only high-confidence stale/duplicate material was removed.
- Files to inspect first: `README.md`, `docs/START_HERE.md`, `docs/development/RELEASE_CHECKLIST.md`, and `docs/core/ARCHITECTURE.md`.
