# Deferred Notes

This file tracks explicit "not yet" decisions so they do not get reintroduced by drift or memory.

## Verifier API

- Do not add a separate validator layer yet.
- Do not add a richer verifier error envelope yet.
- Add those only if external clients actually need structured per-field API errors.

## Verifier Idempotency

- Do not widen the idempotency API contract yet.
- Do not add a dedicated idempotency service yet.
- The current `Idempotency-Key` header is enough until external clients need richer retry semantics.

## Verifier Migrations

- Do not split migration execution from normal startup yet for local dev and MVP self-hosting.
- Add a migration-only rollout path or startup flag before multi-replica production deployment.

## Verifier Signing

- Do not add public-key receipt signatures yet.
- Do not add automated signing key rotation, key IDs, or KMS/Vault integration yet.
- The current `ReceiptSigningKey` HMAC path is enough until receipts must be independently verified outside the server boundary.
- Identifying which key signed a receipt/report via its stable SHA-256 fingerprint is now supported, but automated rotation/management is deferred.

## Verifier Auth

- Do not add users, roles, JWT/OIDC, or institution RBAC yet.
- The current `VerifierSecurity:ApiKey` guard is only a shared-secret MVP barrier.
- Add real identity and RBAC before exposing a verifier to broad institutional users.

## Package Submission

- Do not store `.owspkg` blobs in PostgreSQL.
- Do not add local filesystem package storage as the production path.
- The current `POST /packages` endpoint registers object storage metadata only; package upload and server-side package verification workers are still separate follow-up work.
- Actual package blob upload and server-side package verification worker should be added when the object storage credentials/upload flow is defined.

## Watcher / Hosts

- Do not build full IDE plugins yet.
- Do not build background service installation yet.
- Add those only after the always-on watcher lifecycle is clearer.

## Infrastructure

- Do not add Redis as a source of truth.
- Do not add Kubernetes, NATS, or object-storage plumbing to the MVP verifier path yet.
- Add those only when the current PostgreSQL-backed verifier boundary is no longer enough.
- Docker image runtime validation may be blocked in restricted local environments when Docker config or buildx state is not accessible.
- Revisit image build/run validation on a machine with normal Docker permissions rather than patching around local access-denied noise.
- The verifier image build was validated locally on 2026-06-19; full container runtime validation with PostgreSQL wiring is still deferred.

## Local Dev Runner

- Do not add broader environment bootstrapping beyond verifier auto-build yet.
- The current local verifier helpers may auto-build the verifier server, but they do not install SDKs, Docker, or PostgreSQL for the operator.
- Build-owned script generation is intentionally just platform-specific emitted wrappers, not a custom script generator subsystem.
- Docker access warning suppression in restricted environments is deferred for now.
- Revisit that only if the warning noise starts hiding real startup failures.
