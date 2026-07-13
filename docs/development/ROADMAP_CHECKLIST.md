# OWS Roadmap Checklist

Whenever a feature is added, changed, deferred, or completed, update this checklist in the same commit.

> [!IMPORTANT]
> **Event presence is evidence of recorded activity. Event absence is not proof of misconduct.**
>
> PackageCreated records local packaging after the artifact is written and may appear in the next timeline/package state.

## Next Recommended Step

Reduce the public command and code surface now that package signing is available.

**Next milestone:** Public Alpha Truth Audit and Release Candidate v0.1.

## 1. Current MVP Status

- [x] Local project initialization
- [x] `ows watch` persistent file-system watcher with polling fallback
- [x] `.owspkg` creation
- [x] Local and remote session start
- [x] Local and remote checkpoint issuance
- [x] Package verification with trust grading
- [x] Live verifier cross-checking
- [x] Basic text reports
- [ ] Verifier server exists but is not production-grade - Status: Partial

## 2. Technical Demo Readiness

- [x] End-to-end local demo path exists
- [x] Local PostgreSQL verifier smoke testing
- [x] Local verifier helper scripts
- [x] Cold-start from a clean clone validated and documented
- [x] Demo path in restricted environments validated and documented

## 3. Local Verifier Dev Loop

- [x] Read-only local verifier preflight helper
- [x] Verifier helpers auto-build missing local server output
- [x] Foreground verifier run helper
- [x] Background verifier start helper
- [x] Verifier status helper
- [x] Verifier logs helper
- [x] Verifier smoke test helper
- [x] Verifier stop helper
- [x] Local verifier helpers respect optional API key guard
- [x] Local verifier smoke test covers package metadata path
- [x] Local dev loop cold-start and restricted-environment validated

- [x] OWS event vocabulary defined
- [x] File-system event capture implemented
- [x] Event hash-chain timeline implemented
- [x] Build/test/program execution event emitters
- [x] Watcher start/stop/interruption/recovery lifecycle event emitters
- [ ] Large insertion detection
- [x] PackageCreated event emission
- [x] Manifest packaging
- [x] Timeline packaging
- [x] Empty version graph placeholder packaged for forward compatibility
- [ ] Real work version graph nodes/edges/validation
- [x] Packaged session metadata
- [x] Packaged receipts
- [x] Artifact hash verification
- [x] Canonical logical package-root hash
- [x] Optional local RSA package signing and offline verification
- [x] Explicit signed, unsigned, and invalid signature states
- [x] Shared `.owsignore` rules applied to package manifests and archives
- [x] Ignored secrets/build/dependency/log paths excluded from packages
- [x] Explicit binary artifacts remain opaque and hash-only
- [x] Ignored-package integration test verifies locally
- [x] Receipt chain verification
- [x] Trust grades `Verified`, `Unverified`, and `Invalid`
- [x] `Degraded` trust state exists and is integrated with lease gap policy
- [x] Formal package specification

## 5. Local Watcher and Capture Fidelity

- [x] Persistent always-on watcher
- [x] Heartbeat and lease model
- [x] Persistent file-system watcher capture
- [x] Event hash-chain timeline
- [x] Explicit build/test/program event commands
- [x] Watcher started/stopped/interrupted/recovered lifecycle event emitters
- [x] Observation Gap and Recovery Scan (gap duration, previous state tracking, exclusions, atomic snapshots, absolute deltas, trust degradation)
- [x] Snapshot hash commitments in the timeline (`SnapshotUpdated`)
- [x] Recovery snapshot mismatch/unbound degradation without misconduct verdicts
- [x] Explicit project registry stores only initialized project roots
- [x] CLI-run agent host watches multiple registered projects and prunes missing roots
- [x] Secure local IPC for CLI/agent coordination
- [x] Windows-first installable OS service/host lifecycle with reversible scripts
- [x] `.owsignore` created by `ows init` without overwriting existing project rules
- [x] Centralized ignore matching for default rules, comments, directory patterns, wildcards, and normalized separators
- [x] Ignore-engine unit coverage and initialization preservation coverage
- [ ] Host-specific build/test/run capture in a separate adapter
- [ ] Large insertion detection
- [x] Host-managed long-running watcher lifecycle on Windows and foreground cross-platform host

## 6. Remote Verifier and Storage

- [x] PostgreSQL verifier storage foundation
- [x] App-owned verifier migrations
- [x] `Idempotency-Key` support
- [x] MVP verifier receipt signing key support
- [x] Optional shared-key verifier API guard
- [ ] Production-grade verifier hosting
- [x] Server signing key management hardening
- [ ] Auth and RBAC - Status: Partial
- [x] Operator API keys
- [x] Institution-scoped reviewer API keys
- [x] Read-only reviewer access to verification resources
- [x] Persistent API key records
- [x] API key hash storage only
- [x] API key revocation
- [x] API key expiry
- [x] API key last-used tracking
- [x] Legacy shared-key compatibility
- [x] OIDC/JWT bearer foundation
- [ ] Full interactive SSO login, browser sessions, dashboard auth flows, and SAML - Deferred
- [x] Observability v0.1

## 6A. Auth and RBAC v0.1

- [x] Operator role implemented
- [x] InstructorReviewer role implemented
- [x] InstitutionAdmin role
- [x] StudentClient role
- [x] Institution scoping enforced for package metadata and verification resources
- [x] Institution scoping enforced for verifier resources using opaque context metadata
- [x] Reviewer read-only policy enforced
- [x] Operator API key lifecycle endpoints
- [x] Operator documentation for pilot key management

## 6B. Observability v0.1

- [x] Structured verifier request logs
- [x] `X-Request-Id` correlation id propagation
- [x] Safe audit event store
- [x] Operator-only `GET /audit/events`
- [x] `GET /diagnostics/summary`
- [x] `/ready` dependency detail response
- [x] Secret-safe audit and diagnostics output
- [x] Prometheus-compatible metrics endpoint
- [x] Optional Grafana/Loki/Promtail overlay
- [ ] Mandatory/production monitoring stack - Deferred

## 7. Package Submission and Server-Side Verification

- [x] Server-side package metadata submission
- [x] Server-side package verification API surface
- [x] Remote package object metadata registration
- [x] Remote package metadata retrieval
- [x] Remote package metadata session lookup
- [x] Package metadata session-head anchoring
- [x] Package metadata idempotent retries
- [x] Remote package anchor workflow

## 7A. Package Intake Operations v0.1

- [x] Durable local package blob storage abstraction
- [x] Actual `.owspkg` blob upload endpoint
- [x] Package hash and size computed during upload
- [x] Package blobs stored outside PostgreSQL
- [x] Package upload max-size enforcement
- [x] Package upload shape validation
- [x] Worker-backed package verification job boundary
- [x] Durable package verification job store
- [x] Startup recovery for stale running package verification jobs
- [x] Persisted package verification status and result
- [x] Reviewer-safe package status/report reads
- [x] Package verification audit events
- [x] Package verification diagnostics counters
- [x] Built-in endpoint rate limiting
- [x] Dedicated scoped verifier-resource rate limiting
- [x] Scoped upload authorization before blob persistence
- [x] Archive hardening for entry count, duplicate paths, unsafe paths, and expansion limits
- [x] Scoped verifier write audit events
- [x] Bounded audit query limit (`GET /audit/events` max 500)

## 8. Reports and Reviewer Guidance

- [x] Basic text report output
- [x] JSON report output
- [x] Reviewer report output
- [x] Better degraded-state review guidance

## 9. Security, Threat Model, and Privacy

- [x] Formal threat model
- [x] Privacy and data retention documentation
- [x] Security hardening guidance for verifier operations
- [x] Clear institutional trust-boundary guidance

## 10. Deployment and Self-Hosting

- [x] Local Docker and PostgreSQL dev path
- [x] Deployment supports local Docker/PostgreSQL and production Docker Compose
- [x] Verifier server Docker image
- [x] Production Compose packaging
- [x] Self-hosting operator guide

## 11. External Management Boundaries

- [x] OWS does not own institutions, courses, classes, students, rosters, or LMS records.
- [x] Session and package APIs accept optional opaque external context identifiers.
- [x] Verifier scoping uses caller-supplied institution and student identifiers without resolving management records.
- [x] Reports expose opaque context identifiers only; human-readable names and roster resolution belong to future projects.
- [ ] Separate management/integration project, if needed

## 13. Deferred UI and Host Boundaries

- [x] OWS Core and CLI remain independent of any IDE.
- [x] `Ows.Desktop` remains a placeholder until explicitly requested.
- [x] Host metadata remains generic and protocol-focused.
- [ ] Optional host adapters, if ever needed, belong in separate projects.

## 14. Deferred and Future Technologies

- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] Broad infrastructure expansion before the verifier workflow is stable
- [ ] External object storage / S3 package blob backend

## 15. Verifier Operations Hardening v0.1

- [x] Backup model documented (`docs/operations/BACKUP_RESTORE.md`)
- [x] Restore order documented (`docs/operations/BACKUP_RESTORE.md`)
- [x] Recovery failure modes documented (`docs/operations/BACKUP_RESTORE.md`)
- [x] Package blob backup documented
- [x] Signing key custody documented (`docs/operations/SECURITY_HARDENING.md`)
- [x] Restore and verify known package drill documented (`docs/operations/BACKUP_RESTORE.md`)
- [x] `docs/operations/OPERATIONS_RUNBOOK.md` created
- [x] `scripts/windows/verify-ops-readiness.ps1` and `scripts/unix/verify-ops-readiness.sh` created
- [x] Package storage diagnostics (`packageStorageConfigured`, `packageStorageReady`, `packageBlobCount`)
- [x] Signing key fingerprint diagnostics (`signingKeyFingerprintPresent` in `/diagnostics/summary`)
- [x] `package.blob.missing` audit event implemented in worker
- [x] Startup recovery for stale Running jobs (existing, documented in runbook)
- [x] `docs/operations/SELF_HOSTED_COMPOSE.md` updated with operator resource cross-references
- [x] `docs/operations/TROUBLESHOOTING.md` updated with restore/signing key failure modes

## 16. IDE Drift Removal v0.1

- [x] Removed the VS Code extension from the active repository.
- [x] Removed Rider/IntelliJ integration design from active documentation.
- [x] Removed IDE compilation from the release regression gate.
- [x] Removed IDE workflow and status UI claims from student and pilot documentation.
- [x] CLI/Core workflows remain independently buildable and testable.

## 17. Student Workflow Hardening v0.1

- [x] Stale watcher PID lock recovery & name validation
- [x] Duplicate watcher start prevention
- [x] Safe watcher stop when already stopped
- [x] Missing or moved project directory checks (DirectoryNotFoundException)
- [x] Offline verifier background heartbeat loop and state persistence
- [x] Heartbeat and checkpoint error state tracking
- [x] Package upload timeout (60 seconds) and specific HTTP error code parsing (400, 413, 401, 403)
- [x] CLI status mapping (WatchingLocalOnly, SessionActive, VerifierOffline, HeartbeatFailing, Degraded, Error)
- [x] Student-friendly exception translation
- [x] Comprehensive watcher and CLI status/redaction tests
- [x] Updated troubleshooting and student workflow documentation

## 18. Pilot End-to-End Validation v0.1

- [x] Pilot fixture setup scripts (`scripts/windows/setup-pilot-fixture.ps1`, `scripts/unix/setup-pilot-fixture.sh`)
- [x] Main reviewer/sysadmin walkthrough (`docs/workflows/PILOT_DEMO.md`)
- [x] Student CLI workflow validation documented
- [x] StudentClient session/package/upload validation documented
- [x] Reviewer report workflow validation documented
- [x] Audit and diagnostics workflow validation documented
- [x] Heartbeat lifecycle validation documented
- [x] Negative-path validation checklist documented
- [x] Full live pilot dry run against the local PostgreSQL-backed verifier path

## 19. Regression Gate and Release Candidate v0.1

- [x] `docs/development/RELEASE_CHECKLIST.md`
- [x] `docs/development/REGRESSION_GATE.md`
- [x] Automated release-gate scripts (`scripts/windows/run-release-regression-gate.ps1`, `scripts/unix/run-release-regression-gate.sh`)
- [x] Release-candidate evidence scripts (`scripts/windows/collect-release-candidate-evidence.ps1`, `scripts/unix/collect-release-candidate-evidence.sh`)
- [x] Known-good local pilot path documented
- [x] Automated vs manual checks documented
- [x] Fixture repeatability/reset guidance documented
- [x] Latest live dry run passed and remains checked
- [x] Latest release gate passed

## Multi-Instance Verifier Deployment v0.1

- [x] deployment model documented
- [x] API-only vs worker mode documented
- [x] worker enable/disable config
- [x] DB-safe job claiming
- [x] migration startup guidance
- [x] multi-instance readiness diagnostics
- [x] shared package blob storage guidance
- [x] Compose pattern documented
- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] full interactive SSO, browser sessions, dashboard auth flows, and SAML
- [x] optional Grafana/Loki/Promtail overlay
- [ ] mandatory/production monitoring stack and Alertmanager
- [ ] billing/SaaS features

## External Observability Integration v0.1

- [x] Prometheus scrape config
- [x] Grafana dashboard JSON
- [x] Grafana provisioning
- [x] optional Loki/Promtail config
- [x] optional Compose observability overlay
- [x] observability docs
- [x] privacy/security warnings
- [x] compose config validation
- [ ] mandatory monitoring stack
- [ ] production alerting/Alertmanager
- [ ] Kubernetes monitoring
- [ ] hosted Grafana Cloud integration
- [ ] full interactive SSO, browser sessions, dashboard auth flows, and SAML
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] S3/external object storage
- [ ] billing/SaaS features

## OIDC/JWT Bearer Foundation v0.1

- [x] optional OIDC config model
- [x] claims-to-principal mapper
- [x] role claim mapping
- [x] institution claim mapping
- [x] API key compatibility
- [x] diagnostics/readiness OIDC status
- [x] OIDC/JWT bearer docs
- [x] dual-auth rejection and safe audit event
- [ ] browser login and callback flow
- [ ] dashboard/browser session management
- [ ] SAML
- [ ] dashboard UI
- [ ] user provisioning UI
- [ ] full LMS integration
- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] S3/external object storage
- [ ] billing/SaaS features

