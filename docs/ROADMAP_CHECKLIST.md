# OWS Roadmap Checklist

Whenever a feature is added, changed, deferred, or completed, update this checklist in the same commit.

## Next Recommended Step

Stabilize package submission operations with durable blob upload flow and server-side verification worker execution.

**Next milestone:** Package Intake Operations v0.1.

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

## 4. Core Protocol and Package Format

- [x] Chained timeline event model
- [x] Manifest, timeline, and version graph packaging
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
- [x] Higher-fidelity capture beyond one-shot scans
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
- [ ] Full SSO/OIDC/SAML - Deferred
- [x] Observability v0.1

## 6A. Auth and RBAC v0.1

- [x] Operator role implemented
- [x] InstructorReviewer role implemented
- [ ] InstitutionAdmin role - Deferred
- [ ] StudentClient role - Deferred
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
- [ ] Prometheus/Grafana/Loki - Deferred

## 7. Package Submission and Server-Side Verification

- [x] Server-side package submission
- [x] Server-side package verification
- [x] Remote package object metadata registration
- [x] Remote package metadata retrieval
- [x] Remote package metadata session lookup
- [x] Package metadata session-head anchoring
- [x] Package metadata idempotent retries
- [x] Remote package anchor workflow

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

- [ ] Desktop UI
- [ ] VS Code integration
- [ ] Rider integration
- [ ] Host-specific watcher implementations

## 14. Deferred and Future Technologies

- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] Broad infrastructure expansion before the verifier workflow is stable
- [ ] Actual package blob upload and server-side package verification worker. Add when object storage credentials/upload flow is defined.
