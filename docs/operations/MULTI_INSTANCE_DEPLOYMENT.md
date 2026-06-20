# Multi-Instance Verifier Deployment v0.1

This document defines the minimal multi-instance deployment model currently supported by the Open Work Standard Work Verifier.

The goal is narrow: make multiple verifier instances safe and understandable for pilot deployments without introducing Kubernetes, Redis, NATS, or a full distributed-system control plane.

## Supported Modes

### Mode A: Single-node recommended pilot mode

Use this unless you have a concrete reason not to.

- one verifier instance
- PostgreSQL storage
- local package blob storage
- in-process package verification worker enabled
- automatic migrations may remain enabled for local/dev

This is still the recommended pilot baseline because it has the smallest failure surface.

### Mode B: Multi-API / single-worker mode

This is the supported multi-instance pilot pattern today.

- multiple verifier API instances
- shared PostgreSQL database
- shared package blob storage path or a compatible shared backend mounted at the same verifier storage path
- exactly one instance runs the package verification worker
- all other instances run in API-only mode

Required settings:

- `PackageVerificationWorker:Enabled=true` on exactly one instance
- `PackageVerificationWorker:Enabled=false` on every API-only instance
- `VerifierStorage:ApplyMigrationsOnStartup=false` on steady-state instances

Operational rule:

- run `dotnet Ows.Verifier.Server.dll migrate` once before starting all instances for a new deployment or upgrade

Why this works:

- package metadata and jobs are shared through PostgreSQL
- package verification job claiming uses PostgreSQL-safe row locking with `FOR UPDATE SKIP LOCKED`
- if two workers are started accidentally, they should not normally claim the same pending job

Limits you must not ignore:

- local package storage is only safe here if the path is truly shared across every instance that may read or verify package blobs
- if each instance has its own private local disk, Mode B is not valid

### Mode C: Future distributed mode

Not implemented in v0.1.

Expected future shape:

- S3-compatible package blob backend
- external queue or stronger DB-backed worker leasing model
- Kubernetes or Helm deployment support
- Redis or NATS only if they solve a concrete scaling or operational problem

## Worker Configuration

Primary worker toggle:

- `PackageVerificationWorker:Enabled`

Behavior:

- `true`: instance runs the in-process verification worker
- `false`: instance serves API requests only

Defaults:

- local/dev defaults to `true`
- multi-instance pilots should set it explicitly on every instance

The verifier still accepts uploads when the worker is disabled. Jobs remain `Pending` until a worker-enabled instance claims them.

## Migration Safety

The verifier supports two migration paths:

- explicit bootstrap: `dotnet Ows.Verifier.Server.dll migrate`
- automatic startup migrations: `VerifierStorage:ApplyMigrationsOnStartup=true`

For multi-instance deployments, use the explicit bootstrap path.

Recommended rule:

1. Stop or hold new instances.
2. Run `migrate` once against the shared PostgreSQL database.
3. Start API and worker instances with `VerifierStorage:ApplyMigrationsOnStartup=false`.

Do not rely on every replica racing to migrate the schema. That is unnecessary complexity for this stage.

## Package Blob Storage Rules

Current package blob provider:

- local filesystem storage (`local-file`)

That means one of these must be true:

- the deployment is single-node
- the deployment mounts one shared package storage path into every instance that needs package reads or verification

Not supported yet:

- per-instance private blob directories in a multi-instance deployment
- S3-compatible object storage

Readiness and diagnostics intentionally expose:

- storage provider
- package storage configured/ready state
- worker enabled state
- instance mode
- warnings when the worker is enabled but package storage is unavailable

They do not expose local absolute storage paths.

## Readiness and Diagnostics Expectations

`GET /ready` and `GET /diagnostics/summary` now surface:

- `storageProvider`
- `packageStorageProvider`
- `packageStorageConfigured`
- `packageStorageReady`
- `workerEnabled`
- `instanceMode`
- `applyMigrationsOnStartup`
- `warnings`

Instance mode values today:

- `api+worker`
- `api-only`

## Compose Pattern

See:

- [`deploy/compose/docker-compose.yml`](../../deploy/compose/docker-compose.yml)
- [`deploy/compose/docker-compose.multi-instance.yml`](../../deploy/compose/docker-compose.multi-instance.yml)

The multi-instance compose file demonstrates:

- two API-only verifier containers
- one worker-enabled verifier container
- one shared PostgreSQL database
- one shared package blob volume

It is a deployment pattern, not a claim of full high-availability orchestration.

## Explicit Non-Goals for v0.1

Do not add yet:

- Kubernetes
- Helm
- Redis
- NATS
- QUIC
- full SSO/OIDC/SAML
- Grafana/Loki stack
- billing or SaaS tenancy layers
