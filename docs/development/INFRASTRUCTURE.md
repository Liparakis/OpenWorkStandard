# Infrastructure Responsibilities

## Principle

Pick boring infrastructure with clear responsibility boundaries.

PostgreSQL is the source of truth.
Redis/Valkey is not.
Object storage holds blobs.
NATS JetStream is optional and only justified when a durable internal event pipeline exists.

## PostgreSQL

PostgreSQL is the durable source of truth.

Expected responsibilities:

- verifier API keys and roles
- opaque external context identifiers supplied by callers
- verifier sessions
- checkpoints
- checkpoint receipts
- verifier checkpoints
- verification reports
- package metadata
- audit events

OWS does not own institutions, users, courses, rosters, enrollments, or
assessment-management records. A deployment may use opaque institution,
assessment, student, or course-offering identifiers for authorization and
report metadata, but those values are not resolved against an OWS management
database.

Do not store large `.owspkg` blobs directly in PostgreSQL.

## Redis / Valkey

Redis/Valkey is only for ephemeral shared state across horizontally scaled API instances.

Use cases:

- active session leases
- heartbeats
- rate limits
- idempotency keys
- distributed locks
- temporary checkpoint coordination

Redis must never be treated as authoritative verified history.

## Object Storage

Use S3-compatible object storage for large binary or export content.

Examples:

- `.owspkg` package blobs
- uploaded package archives
- large reports
- export bundles
- optional encrypted starter files

Store metadata in PostgreSQL and blobs in object storage.

Current verifier note:

- the reference verifier server still defaults to local JSON storage for development
- the production-intended verifier backend is PostgreSQL behind the same storage boundary
- package submission metadata is stored in PostgreSQL, but `.owspkg` bytes are expected to be in S3-compatible object storage first

## Optional NATS JetStream

Use NATS JetStream only when durable internal event processing is needed.

Potential events:

- `checkpoint.received`
- `receipt.signed`
- `package.uploaded`
- `package.verified`
- `report.generated`
- `session.degraded`
- `session.expired`

For MVP:

- direct database writes are fine
- do not add NATS unless a clean worker/event boundary already exists

## Service Roles

### `ows-api`

- receives checkpoint/session/package requests
- authenticates callers
- validates payloads
- persists durable state
- returns receipts or verification responses

### `ows-worker`

- performs asynchronous/background jobs
- generates reports
- handles slower blob/storage flows
- consumes internal events later if introduced

### observability stack

- OpenTelemetry for traces/metrics/log correlation
- Prometheus for metrics
- Grafana for dashboards
- Loki for logs

## Self-Hosted Baseline

Small local-first verifier baseline:

- ASP.NET Core verifier API
- background worker
- PostgreSQL
- local package blob storage

Redis/Valkey, S3-compatible storage, NATS, and orchestration are deferred
until the corresponding scale or deployment requirement exists.

## What Not To Do

- do not put durable verified history in Redis
- do not put large package blobs in PostgreSQL
- do not start with custom transport infrastructure
- do not add NATS just because distributed systems look impressive
- do not force production-grade manifests before the server boundary exists
