# OWS Roadmap Checklist

Whenever a feature is added, changed, deferred, or completed, update this checklist in the same commit.

> [!IMPORTANT]
> **Event presence is evidence of recorded activity. Event absence is not proof of misconduct.**
>
> PackageCreated records local packaging after the artifact is written and may appear in the next timeline/package state.

## Next Recommended Step

Cut the v0.1 release-candidate evidence bundle from the latest green gate, then do manual sign-off before adding new UI surfaces or integrations.

**Next milestone:** Regression Gate and Release Candidate v0.1.

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
- [ ] IDE-native build/test/run capture
- [ ] Large insertion detection
- [ ] IDE or host-managed long-running watcher lifecycle

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
- [ ] Full interactive SSO login / SAML - Deferred
- [x] Observability v0.1

## 6A. Auth and RBAC v0.1

- [x] Operator role implemented
- [x] InstructorReviewer role implemented
- [x] InstitutionAdmin role
- [x] StudentClient role
- [x] Institution scoping enforced for package metadata and verification resources
- [x] Institution scoping enforced for education read endpoints
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

## 8. Reports and Professor Review

- [x] Basic text report output
- [x] JSON report output
- [x] Rich professor review report
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

## 11. Multi-Tenant Education Model

- [x] Institutions
- [x] Courses
- [x] Classes
- [x] Students
- [x] Multi-tenant policy model

## 12. Education Workflow Wiring v0.1

- [x] `IEducationStore` interface (Json + Postgres backends)
- [x] `POST /education/institutions`, `GET /education/institutions/{id}`
- [x] `POST /education/courses`, `GET /education/courses/{id}`
- [x] `POST /education/class-groups`, `GET /education/class-groups/{id}`
- [x] `POST /education/course-offerings`, `GET /education/course-offerings/{id}`
- [x] `POST /education/enrollments`, enrollment queries by user and offering
- [x] `POST /education/assessments`, `GET /education/assessments/{id}`
- [x] `POST /education/users`, `GET /education/users/{id}`
- [x] `POST /sessions` accepts optional education context (`institutionId`, `assessmentId`, `studentUserId`, `courseOfferingId`)
- [x] Institution, assessment-to-institution, and student validation on session start
- [x] Education context propagated into `PackageVerificationRequest` on all three verification paths
- [x] `ReportEducationContext` assembled from store lookups and embedded in verification reports
- [x] `Assessment Context` section in JSON and text reports
- [x] `EducationWiringTests` covering store round-trips and session validation logic

## 13. Desktop and IDE Integrations

- [x] Desktop UI v0.1 design spec (`docs/DESKTOP_UI.md`)
- [x] VS Code integration v0.1 (`src/ows-vscode`)
- [x] Rider integration v0.1 design (`docs/RIDER_INTEGRATION.md`)
- [x] Host-specific watcher implementations via `IOwsWatchSessionManager`

## 14. Deferred and Future Technologies

- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] Broad infrastructure expansion before the verifier workflow is stable
- [ ] External object storage / S3 package blob backend

## 15. Verifier Operations Hardening v0.1

- [x] Backup model documented (`docs/BACKUP_RESTORE.md`)
- [x] Restore order documented (`docs/BACKUP_RESTORE.md`)
- [x] Recovery failure modes documented (`docs/BACKUP_RESTORE.md`)
- [x] Package blob backup documented
- [x] Signing key custody documented (`docs/SECURITY_HARDENING.md`)
- [x] Restore and verify known package drill documented (`docs/BACKUP_RESTORE.md`)
- [x] `docs/OPERATIONS_RUNBOOK.md` created
- [x] `scripts/verify-ops-readiness.ps1` created
- [x] Package storage diagnostics (`packageStorageConfigured`, `packageStorageReady`, `packageBlobCount`)
- [x] Signing key fingerprint diagnostics (`signingKeyFingerprintPresent` in `/diagnostics/summary`)
- [x] `package.blob.missing` audit event implemented in worker
- [x] Startup recovery for stale Running jobs (existing, documented in runbook)
- [x] `docs/SELF_HOSTED_COMPOSE.md` updated with operator resource cross-references
- [x] `docs/TROUBLESHOOTING.md` updated with restore/signing key failure modes

## 16. Desktop and IDE Integrations v0.1

- [x] Host-managed watcher lifecycle abstraction (`IOwsWatchSessionManager`)
- [x] Machine-readable CLI status/control (`--json` output format)
- [x] Local project config (`.ows/config.json`)
- [x] VS Code extension commands
- [x] VS Code status bar showing active watcher/session status
- [x] Secure API key storage (VS Code `SecretStorage` and OS Keychain integration)
- [x] Package creation and upload from IDE
- [x] Remote verification status query from IDE
- [x] Minimal Desktop Tray/Status UI spec (`docs/DESKTOP_UI.md`)
- [x] Rider integration design (`docs/RIDER_INTEGRATION.md`)

## 17. Student Workflow Hardening v0.1

- [x] Stale watcher PID lock recovery & name validation
- [x] Duplicate watcher start prevention
- [x] Safe watcher stop when already stopped
- [x] Missing or moved project directory checks (DirectoryNotFoundException)
- [x] Offline verifier background heartbeat loop and state persistence
- [x] Heartbeat and checkpoint error state tracking
- [x] Package upload timeout (60 seconds) and specific HTTP error code parsing (400, 413, 401, 403)
- [x] CLI/extension status mapping (WatchingLocalOnly, SessionActive, VerifierOffline, HeartbeatFailing, Degraded, Error)
- [x] Student-friendly exception translation
- [x] VS Code workspace trust checking
- [x] VS Code multi-folder support (silent fallback vs quickpick vs active editor)
- [x] VS Code spawn error detection (ENOENT)
- [x] VS Code API key and environment variable redactions in logs and error displays
- [x] Comprehensive watcher and CLI status/redaction tests
- [x] Updated troubleshooting and student workflow documentation

## 18. Pilot End-to-End Validation v0.1

- [x] Pilot fixture setup script (`scripts/setup-pilot-fixture.ps1`)
- [x] Main professor/sysadmin walkthrough (`docs/PILOT_DEMO.md`)
- [x] Student CLI workflow validation documented
- [x] Student VS Code workflow validation documented
- [x] StudentClient session/package/upload validation documented
- [x] Reviewer report workflow validation documented
- [x] Audit and diagnostics workflow validation documented
- [x] Heartbeat lifecycle validation documented
- [x] Negative-path validation checklist documented
- [x] Full live pilot dry run against the local PostgreSQL-backed verifier path

## 19. Regression Gate and Release Candidate v0.1

- [x] `docs/RELEASE_CHECKLIST.md`
- [x] `docs/REGRESSION_GATE.md`
- [x] Automated release-gate script (`scripts/run-release-regression-gate.ps1`)
- [x] Release-candidate evidence script (`scripts/collect-release-candidate-evidence.ps1`)
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
- [ ] full SSO/OIDC/SAML
- [ ] Grafana/Loki stack
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
- [ ] SSO/OIDC/SAML
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
- [ ] SAML
- [ ] dashboard UI
- [ ] cookie/browser login flow
- [ ] user provisioning UI
- [ ] full LMS integration
- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] S3/external object storage
- [ ] billing/SaaS features
