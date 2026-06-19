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

## Watcher / Hosts

- Do not build full IDE plugins yet.
- Do not build background service installation yet.
- Add those only after the always-on watcher lifecycle is clearer.

## Infrastructure

- Do not add Redis as a source of truth.
- Do not add Kubernetes, NATS, or object-storage plumbing to the MVP verifier path yet.
- Add those only when the current PostgreSQL-backed verifier boundary is no longer enough.

## Local Dev Runner

- Do not force `dotnet build` inside the local verifier runner yet.
- The current runner expects existing verifier build output and starts PostgreSQL, migrations, and the server only.
- Revisit auto-build only when the local environment no longer trips over machine-level NuGet policy.
- Build-owned script generation is intentionally just platform-specific emitted wrappers, not a custom script generator subsystem.
