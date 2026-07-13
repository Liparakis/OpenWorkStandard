# Durable Decisions

## OWS is a proof-of-work protocol/toolchain, not an LMS

- Date: 2026-07-13
- Status: Accepted
- Context: OWS needs to establish provenance and verification without becoming an academic administration product.
- Decision: Open Work Standard is a local-first proof-of-work protocol/toolchain for academic work provenance, not an LMS.
- Reasoning: Keeping evidence capture and verification separate from institutional administration protects scope, privacy, and architectural clarity.
- Consequences: Course, roster, grading, and other management concerns are outside the OWS core workflow.
- Replaces: None; baseline product boundary.

## Management layers belong in separate future projects

- Date: 2026-07-13
- Status: Accepted
- Context: Education-management concepts can be useful around OWS but are not part of its core responsibility.
- Decision: Management layers belong in separate future projects and must not be pulled into OWS by default.
- Reasoning: Separate ownership and deployment boundaries keep the provenance toolchain small and auditable.
- Consequences: Integrations may consume OWS evidence, but OWS does not own institutional management state.
- Replaces: None; baseline product boundary.

## OWS is IDE-agnostic

- Date: 2026-07-13
- Status: Accepted
- Context: Work may happen in many editors and tools.
- Decision: The core workflow and evidence model must not depend on a specific IDE.
- Reasoning: IDE coupling would narrow adoption and move host-specific concerns into the wrong layer.
- Consequences: IDE integrations remain optional adapters around the local-first core.
- Replaces: None; baseline architecture.

## The filesystem is the primary observation source

- Date: 2026-07-13
- Status: Accepted
- Context: OWS needs a practical, local, cross-platform source of work activity.
- Decision: The filesystem and project-scoped file events are the primary observation source.
- Reasoning: Filesystem observation is available across tools and can remain local-first without collecting keystrokes or private content.
- Consequences: Native watcher signals and a polling fallback are required; absence of events is never treated as misconduct proof.
- Replaces: None; baseline observation model.

## OWS v0 is text-first

- Date: 2026-07-13
- Status: Accepted
- Context: The first usable workflow must work without a desktop UI.
- Decision: OWS v0 prioritizes the CLI and text-based artifacts and reports.
- Reasoning: Text-first surfaces are portable, scriptable, easy to verify, and cheaper to keep honest.
- Consequences: Desktop polish and richer UI remain deferred until the core workflow is stable.
- Replaces: None; baseline product sequencing.

## Binary files are opaque and hash-only

- Date: 2026-07-13
- Status: Accepted
- Context: Binary artifacts may be part of a tracked project but can contain sensitive or unsupported formats.
- Decision: OWS treats binary files as opaque artifacts represented by metadata and hashes, not parsed contents.
- Reasoning: This limits collection and format-specific complexity while preserving integrity checks.
- Consequences: Verification can establish identity/integrity, not semantic claims about binary contents.
- Replaces: None; baseline data boundary.

## Student workflow prioritizes `ows init` and `ows package`

- Date: 2026-07-13
- Status: Accepted
- Context: The student path must be understandable and low-friction.
- Decision: The primary student workflow is project initialization with `ows init` followed by packaging with `ows package`.
- Reasoning: These commands express the smallest useful lifecycle without requiring a ceremony or IDE.
- Consequences: Other commands support the workflow but must not become prerequisites for ordinary use.
- Replaces: None; baseline workflow sequencing.

## Local/offline verification must not require a server

- Date: 2026-07-13
- Status: Accepted
- Context: Network availability and institutional verifier availability cannot be assumed.
- Decision: Local/offline package verification must work without a server; remote verification is optional enrichment.
- Reasoning: Local-first trust and reproducible review require a usable offline path.
- Consequences: Server outages may limit remote cross-checks but must not make local evidence unverifiable.
- Replaces: None; baseline deployment boundary.

## The agent watches only explicitly initialized projects

- Date: 2026-07-13
- Status: Accepted
- Context: Background observation could otherwise expand into unrelated personal-data collection.
- Decision: The OWS agent may watch only projects explicitly initialized for tracking.
- Reasoning: Explicit opt-in creates a clear project boundary and limits privacy exposure.
- Consequences: Uninitialized directories and unrelated personal files remain outside OWS observation.
- Replaces: None; baseline privacy boundary.

## Tamper detection is local and offline in v0

- Date: 2026-07-13
- Status: Accepted
- Context: The earlier remote verifier was an additional anchoring mechanism for detecting later tampering, but OWS has not been publicly released and must remain useful without hosted infrastructure.
- Decision: OWS v0 detects package tampering locally through chained timeline hashes, artifact hashes, canonical package-root hashes, and optional offline package signatures. A remote verifier or server-side anchor is outside the v0 contract.
- Reasoning: Local integrity checks preserve the core proof-of-work property without adding deployment, authentication, privacy, or availability dependencies.
- Consequences: Offline verification is authoritative for package integrity; a future hosted project may add independent anchoring as optional enrichment without becoming an OWS Core dependency.
- Replaces: None; clarifies the local/offline verification boundary.

## No manual start, stop, or checkpoint ceremony in the intended workflow

- Date: 2026-07-13
- Status: Accepted
- Context: Evidence capture should reflect ordinary work rather than an artificial reporting ritual.
- Decision: The intended workflow must not require users to manually start, stop, or checkpoint work as a routine ceremony.
- Reasoning: Ceremony increases friction, creates gaps, and turns the tool into a compliance burden.
- Consequences: Lifecycle automation and recovery semantics must handle interruptions honestly; explicit control commands may still exist for diagnostics or exceptional operations.
- Replaces: None; baseline workflow principle.

## Education-management removal disposition

- Date: 2026-07-13
- Status: Accepted
- Context: The working tree removes the education domain, stores, endpoints, and related tests, but leaves store-dependent verifier code and documentation references. The accepted product boundary excludes management layers from OWS.
- Decision: Complete the removal, replace store-backed session/report resolution with metadata-only behavior, and retain opaque institution/assessment/student identifiers only where required for verifier scoping, package metadata, or audit trails.
- Reasoning: Restoring the deleted management layer would contradict the accepted OWS boundary; deleting all identifiers would unnecessarily break existing trust-boundary metadata.
- Consequences: Human-readable institutional names and roster/course resolution move outside OWS; build, tests, API docs, workflows, and roadmap entries must be reconciled in this phase.
- Replaces: None; accepted scope clarification.

## Local verifier readiness and shutdown own the full process lifecycle

- Date: 2026-07-13
- Status: Superseded
- Context: A TCP-open PostgreSQL port can still be in crash recovery, and stopping the verifier wrapper can leave its `dotnet` child process listening on the configured port.
- Decision: Local verifier helpers must wait for PostgreSQL health readiness before migrations and must stop the full managed verifier process tree, including verified orphaned verifier processes from this workspace.
- Reasoning: Release and local development checks must be deterministic from cold startup and must not leave hidden processes that poison the next run.
- Consequences: Windows and Unix helpers include bounded readiness waits; Windows shutdown matches only the workspace verifier DLL when cleaning an orphan.
- Replaces: Superseded when the unreleased hosted verifier and its helpers were removed from OWS v0.

## IDE adapters are separate projects outside OWS core

- Date: 2026-07-13
- Status: Accepted
- Context: The repository had a VS Code implementation and Rider/IntelliJ integration designs that made editor hosts part of the active product and release path.
- Decision: OWS Core, CLI, filesystem observation, packaging, and offline verification must not depend on IDE integrations. Any future editor or host adapter belongs in a separate project.
- Reasoning: IDE independence is required for filesystem-first use across editors and keeps the protocol/toolchain small and auditable.
- Consequences: IDE-specific code, release checks, settings, and workflow docs are removed from active OWS scope; generic host metadata may remain protocol data.
- Replaces: None; reinforces the accepted IDE-agnostic decision.

## Agent registration is local, explicit, and project-root scoped

- Date: 2026-07-13
- Status: Superseded
- Context: OWS needs transparent background observation without scanning unrelated personal files or making the verifier server mandatory.
- Decision: `ows init` registers only that absolute project root in a user-local JSON registry. The agent host watches only registered initialized roots and prunes missing roots; the first host is exposed as the diagnostic `ows agent run` command.
- Reasoning: A small local registry reuses the existing watcher/recovery pipeline and makes opt-in scope inspectable without adding a network service.
- Consequences: Secure local IPC and OS service installation remain follow-up work; the current CLI exposes only `ows agent run` and `ows agent service` for Agent lifecycle diagnostics/hosting.
- Replaces: Superseded by `Agent registration is local and service-compatible` below.

## Agent registration is local and service-compatible

- Date: 2026-07-13
- Status: Accepted
- Context: OWS must transparently observe only projects that the user explicitly initializes, including when the Windows SCM Agent runs as LocalSystem.
- Decision: `ows init` registers only that absolute project root in the shared local registry. The Agent watches only registered initialized roots, prunes missing roots, and exposes `ows agent run` as the diagnostic host while `Ows.Setup.exe` provides the Windows service host.
- Reasoning: One explicit registry and one project boundary keep observation local, inspectable, and compatible with the silent Windows service.
- Consequences: Uninitialized directories remain outside observation; secure local IPC and non-Windows service adapters remain future work.
- Replaces: `Agent registration is local, explicit, and project-root scoped`.

## Windows-first Agent service uses the real Service Control Manager host

- Date: 2026-07-13
- Status: Superseded
- Context: A console process launched by `New-Service` would not receive reliable Windows service stop/restart semantics, and a LocalSystem service would not share the user's project registry.
- Decision: The SCM adapter remains available for managed deployments, but it is not the normal OWS bootstrap because SCM account installation requires credentials and elevation.
- Reasoning: This preserves SCM lifecycle behavior, user-local scope, and the existing current-user IPC boundary without moving service concerns into `Ows.Core`.
- Consequences: The normal Windows path uses a passwordless per-user Scheduled Task; Linux/macOS service adapters remain deferred; no private credentials are stored by repository scripts.
- Replaces: The earlier CLI-run host-only boundary for Windows service installation; superseded by `Normal Windows bootstrap uses a per-user Scheduled Task`.

## Normal Windows bootstrap uses a per-user Scheduled Task

- Date: 2026-07-13
- Status: Superseded
- Context: A normal student bootstrap must not ask students to discover or provide Windows service-account credentials, while the Agent must use the same user-local registry and project permissions.
- Decision: `scripts/windows/install-ows-agent.ps1` registers a current-user logon task, starts `ows agent run` immediately, and provides matching status/uninstall scripts. The SCM host remains an optional managed-deployment adapter.
- Reasoning: Task Scheduler can launch the Agent under the interactive user without storing or validating a password, preserving project scope and removing unnecessary setup friction.
- Consequences: The normal Agent starts at that user's logon and runs only while that user session is active; service-at-boot behavior is optional and not required for the student workflow.
- Replaces: `Windows-first Agent service uses the real Service Control Manager host` as the default bootstrap decision.

## Windows setup installs the silent SCM Agent service

- Date: 2026-07-13
- Status: Superseded
- Context: The intended product experience is a double-click `Ows.Setup.exe` that installs the Agent, exposes it in Services.msc, starts it silently, and provides a complete uninstall option.
- Decision: `Ows.Setup.exe` is a self-contained Windows GUI-subsystem executable that installs itself under Program Files, registers `OwsAgent` with the Service Control Manager as LocalSystem, starts it automatically, and removes the service and installed files on `--uninstall`. Project `.ows` folders are never deleted.
- Reasoning: SCM provides the requested start/stop/pause/recovery lifecycle without a console window or service-account password. A machine-scoped registry under `%ProgramData%` lets the LocalSystem service and user CLI share explicit project registrations.
- Consequences: Windows setup requires UAC Administrator approval; local users can register explicit project roots in the shared registry; the Windows-only setup/service boundary stays outside `Ows.Core`. The earlier Scheduled Task bootstrap is removed from the product path.
- Replaces: `Normal Windows bootstrap uses a per-user Scheduled Task`.

## Uninstall explicitly chooses whether to remove shared Agent data

- Date: 2026-07-13
- Status: Accepted
- Context: Users asked for a normal Installed apps uninstall while project evidence must remain safe by default.
- Decision: `--uninstall` prompts whether to remove the shared Agent registry; `--uninstall --purge-data` selects removal directly. Both paths remove the SCM service and installed setup files, and neither removes project `.ows` folders.
- Reasoning: The installed program should be removable through native Windows flows without silently deleting shared registrations or project evidence.
- Consequences: Add/Remove Programs may show one short choice prompt; unattended cleanup must pass `--purge-data` explicitly.
- Replaces: The earlier decision that only an explicit `--purge-data` argument could select shared registry deletion.

## Agent-unavailable initialization is recoverable

- Date: 2026-07-13
- Status: Accepted
- Context: `ows init` must register the project before the Agent may be installed or started, so hard-failing initialization would make recovery awkward and break the primary workflow.
- Decision: `ows init` always retains valid local metadata and registration, reports `AgentUnavailable` with an actionable start/install message when IPC ping fails, and remains safe to retry.
- Reasoning: Initialization is local project setup; Agent availability is a separate lifecycle condition and must not destroy or hide the project boundary.
- Consequences: The CLI status is explicit, package creation can fall back to local state, and service setup can happen after initialization.
- Replaces: None; clarifies the accepted `ows init` workflow.

## Package signatures are optional and offline-verifiable

- Date: 2026-07-13
- Status: Superseded
- Context: OWS needs stronger package authenticity without making a verifier server or hosted key service mandatory, while existing unsigned packages must remain usable.
- Decision: Packages always carry a canonical logical root hash when produced by the current builder; `ows package --sign` adds an RSA-SHA256 signature and public-key metadata. Offline verification reports `Valid`, `Unsigned`, `UnsignedLegacy`, or `Invalid` signature status while retaining existing trust grades.
- Reasoning: Established RSA primitives provide a small, auditable authenticity layer; separating signature status from existing receipt/continuity trust preserves backward compatibility and honest semantics.
- Consequences: ZIP order and timestamps do not affect the logical root; unsigned packages are weaker but valid; local key rotation/revocation remains manual and private keys remain user-local.
- Replaces: Superseded by `Package signatures are optional and offline-verifiable in v0` below.

## Package signatures are optional and offline-verifiable in v0

- Date: 2026-07-13
- Status: Accepted
- Context: OWS needs package authenticity and tamper detection without a verifier server, hosted key service, or compatibility burden for unreleased package formats.
- Decision: Packages carry a canonical logical root hash; `ows package --sign` adds an RSA-SHA256 signature and public-key metadata. Offline verification reports only `Valid`, `Unsigned`, or `Invalid` signature status alongside local timeline, artifact, and package-root integrity results.
- Reasoning: The local hash/signature chain is sufficient for v0 integrity checks and keeps the proof-of-work path offline and auditable.
- Consequences: Unsigned packages remain usable but weaker; hosted anchoring, key rotation, and revocation remain future work outside OWS Core.
- Replaces: `Package signatures are optional and offline-verifiable`.

## Reviewer commands accept explicit package paths

- Date: 2026-07-13
- Status: Accepted
- Context: Reviewer workflows may run outside the project directory that produced a package.
- Decision: `ows verify`, `ows inspect`, and `ows report` accept an optional positional `.owspkg` path and retain current-directory defaults for compatibility.
- Reasoning: Explicit package paths make offline review scriptable and match the documented reviewer workflow without adding a new command or server dependency.
- Consequences: Reports are written beside the selected package; `ows inspect` exposes manifest, artifact metadata, timeline events, inferred activity periods, and integrity findings without reading raw file contents.
- Replaces: None.

## Timeline retention requires a chain-preserving package design

- Date: 2026-07-13
- Status: Pending
- Context: The Agent appends evidence events indefinitely, while lossy truncation would break offline continuity verification and the current package format has one timeline entry.
- Decision: Do not add silent timeline truncation or ad hoc rotation in v0.1; design retention/compaction as a package-format change that preserves verifiable chain boundaries.
- Reasoning: Dropping old events would make long-lived projects look cleaner by weakening the evidence model, and an unreviewed multi-file timeline format would be a larger security change than this release phase warrants.
- Consequences: `.ows/timeline.jsonl` can grow for very long-lived projects; retention/compaction remains explicit future work and must update package verification together.
- Replaces: None.

## Unreleased legacy behavior is disposable

- Date: 2026-07-13
- Status: Accepted
- Context: OWS has not been publicly released, but the repository still contains hidden ceremony commands, compatibility aliases, superseded bootstrap paths, and supporting documentation.
- Decision: Treat the current local-first proof-of-work workflow as the only contract. Remove unreleased legacy behavior, aliases, obsolete bootstrap paths, and dead tests/docs instead of preserving compatibility for them.
- Reasoning: Compatibility with an unreleased design adds surface area and ambiguity without protecting users; deletion makes `ows init` → `ows package` → offline verification the honest product path.
- Consequences: Existing internal callers and tests must be migrated or deleted in the same change; public APIs may be reduced when their only purpose is legacy behavior.
- Replaces: None.
