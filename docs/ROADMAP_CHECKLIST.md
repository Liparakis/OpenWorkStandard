# OWS Roadmap Checklist

Whenever a feature is added, changed, deferred, or completed, update this checklist in the same commit.

## Next Recommended Step

Harden verifier operational security in small steps, then add server-side package submission and verification only after the verifier is reliable.

## 1. Current MVP Status

- [x] Local project initialization
- [ ] `ows watch` one-shot capture only - Status: Partial
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
- [ ] Cold-start from a clean clone needs more operator validation - Status: Partial
- [ ] Demo path in restricted environments needs more validation

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
- [ ] Local dev loop exists but still needs cold-start and restricted-environment hardening - Status: Partial

## 4. Core Protocol and Package Format

- [x] Chained timeline event model
- [x] Manifest, timeline, and version graph packaging
- [x] Packaged session metadata
- [x] Packaged receipts
- [x] Artifact hash verification
- [x] Receipt chain verification
- [x] Trust grades `Verified`, `Unverified`, and `Invalid`
- [ ] `Degraded` trust state exists but is not meaningful policy yet - Status: Partial
- [x] Formal package specification

## 5. Local Watcher and Capture Fidelity

- [ ] Persistent always-on watcher
- [ ] Heartbeat and lease model
- [ ] Higher-fidelity capture beyond one-shot scans
- [ ] IDE or host-managed long-running watcher lifecycle

## 6. Remote Verifier and Storage

- [x] PostgreSQL verifier storage foundation
- [x] App-owned verifier migrations
- [x] `Idempotency-Key` support
- [x] MVP verifier receipt signing key support
- [x] Optional shared-key verifier API guard
- [ ] Production-grade verifier hosting
- [ ] Server signing key management hardening - Status: Partial
- [ ] Auth and RBAC - Status: Partial
- [ ] Observability - Status: Partial

## 7. Package Submission and Server-Side Verification

- [ ] Server-side package submission - Status: Partial
- [ ] Server-side package verification
- [x] Remote package object metadata registration
- [ ] Remote package anchor workflow - Status: Partial

## 8. Reports and Professor Review

- [x] Basic text report output
- [x] JSON report output
- [ ] Rich professor review report - Status: Partial
- [x] Better degraded-state review guidance

## 9. Security, Threat Model, and Privacy

- [x] Formal threat model
- [x] Privacy and data retention documentation
- [x] Security hardening guidance for verifier operations
- [x] Clear institutional trust-boundary guidance

## 10. Deployment and Self-Hosting

- [x] Local Docker and PostgreSQL dev path
- [ ] Deployment exists only for local Docker/PostgreSQL today - Status: Partial
- [x] Verifier server Docker image
- [ ] Production Compose or Helm packaging
- [x] Self-hosting operator guide

## 11. Multi-Tenant Education Model

- [ ] Institutions
- [ ] Courses
- [ ] Classes
- [ ] Students
- [ ] Multi-tenant policy model

## 12. Desktop and IDE Integrations

- [ ] Desktop UI
- [ ] VS Code integration
- [ ] Rider integration
- [ ] Host-specific watcher implementations

## 13. Deferred and Future Technologies

- [ ] Kubernetes
- [ ] Redis
- [ ] NATS
- [ ] QUIC
- [ ] Broad infrastructure expansion before the verifier workflow is stable
