# Security Model

## Security Posture

OWS is designed to be tamper-evident, not magically tamper-proof on a student-owned machine.

What OWS should claim:

- local evidence can be made tamper-evident
- remote notarization can strengthen trust
- a clean locally valid package is not the same as a fully verified package
- missing receipts or broken leases should degrade trust rather than pretending certainty

What OWS should not claim:

- that it prevents all cheating
- that the local machine is trustworthy
- that local keys are impossible to inspect
- that absence of evidence is proof of misconduct

## Threat Model

The relevant threat model includes:

- modifying timeline events after capture
- reordering, duplicating, or deleting local events
- modifying packaged artifacts after packaging
- forging or replaying receipt material
- stopping the watcher and continuing work off-record
- retry storms, duplicate checkpoints, and duplicate package uploads
- server or infrastructure outages during active sessions

## Trust Boundary

The student machine is not the final authority.

That means:

- local capture is evidence collection
- the remote verifier is the trust boundary
- final verification depends on whether local timeline state matches the verifier's receipt chain

## Current Implemented Protections

The current repository already implements:

- SHA-256 hashing primitives
- packaged timeline/version-graph/artifact integrity validation
- tamper-evident local event chaining in `timeline.jsonl`
- in-memory receipt chain issuance/validation helpers
- optional packaged receipt-chain verification
- trust grading with `Verified`, `Unverified`, and `Invalid`

Current trust behavior:

- local-only valid packages are `Unverified`
- packages with valid matching receipts can be `Verified`
- structural, hash, event-chain, or receipt-chain failures are `Invalid`

## Secure Transport Direction

Do not begin with custom TCP or DTLS.

Preferred rollout:

1. HTTPS REST over TLS
2. gRPC over HTTP/2 TLS
3. HTTP/3 over QUIC where supported
4. raw QUIC only as a future experiment if still justified

Why:

- TLS termination and certificate management are standard
- HTTP transport fits university networks better than bespoke protocols
- QUIC/HTTP3 is promising but must remain optional because some networks block or degrade UDP
- fallback to HTTP/2 or HTTP/1.1 is mandatory

## Identity and Authorization

Directional security requirements:

- TLS everywhere
- JWT/OIDC for authentication
- institution SSO later via OIDC or SAML
- RBAC for Student, Instructor, Course Admin, Institution Admin, and System Admin
- server-enforced minimum client versions
- signed client releases later
- audit logs for security-sensitive operations
- secrets managed outside the repo

mTLS can come later for official clients or device-bound sessions, but it is not the MVP baseline.

## Session Lease Model

The verifier may issue short-lived session leases/tokens.

Purpose:

- bind useful participation to a live verified session
- allow trust degradation when checkpointing stops
- avoid pretending that local participation alone is authoritative

If checkpointing stops:

- the lease may expire
- the session may become degraded or unverified
- later work may fail to receive clean verified status

## Verification States

OWS should document and surface these states consistently:

- `Verified`: local timeline and server receipts match with no meaningful gaps
- `Degraded`: minor gaps or suspicious but explainable issues
- `Unverified`: watcher stopped, receipts missing, or file changes occurred during an untrusted interval
- `Invalid`: package structure, hash chain, event chain, or receipt chain is broken

Current code supports:

- `Verified`
- `Unverified`
- `Invalid`

`Degraded` is a reserved policy state for later session/lease/gap handling.

## Failure Modes To Handle

Documented failure cases include:

- API pod dies during an active session
- Redis restarts and loses ephemeral keys
- PostgreSQL becomes unavailable
- object storage becomes unavailable
- client network drops mid-session
- HTTP/3/QUIC is blocked by a campus network
- checkpoint is duplicated or retried
- package is uploaded twice
- worker crashes during report generation

The system must fail in a way that preserves honest uncertainty:

- durable history should survive API restarts
- retries should be idempotent
- temporary failures should degrade trust when appropriate
- reports should say what could not be verified

Current caveat:

- the verifier is not yet a strong trust boundary while receipt issuance still uses the JSON development store
- stronger trust claims depend on PostgreSQL-backed durable receipt issuance

## Secrets and Supply Chain

Directional hardening requirements:

- secret management via cloud secret managers, Vault, or External Secrets
- container/image signing later
- SBOM generation later
- dependency/update hygiene
- auditability of release provenance

These are production hardening concerns, not reasons to invent custom cryptographic plumbing in the MVP.
