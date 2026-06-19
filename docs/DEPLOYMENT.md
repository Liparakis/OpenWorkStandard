# Deployment Model

## Goal

OWS must support:

- Docker-based local development
- Kubernetes-based self-hosted deployment
- optional managed hosting

The deployment model should preserve the core trust split:

- clients observe
- servers notarize
- durable state lives in the right backend

## Local Development

Use Docker Compose for the local stack.

Recommended local services:

- `ows-api`
- `ows-worker`
- `postgres`
- `redis` or `valkey`
- `minio`
- optional `nats`

Preferred local container root on this machine:

- `D:\Containers\OWS`

Example topology:

```text
developer machine
├─ ows-api
├─ ows-worker
├─ postgres
├─ redis/valkey
├─ minio
└─ optional nats
```

Local development priorities:

- predictable startup
- inspectable logs
- no managed-cloud dependency
- easy reset/rebuild loop

## Self-Hosted Production

Use Kubernetes plus Helm for self-hosted production.

Recommended runtime pieces:

- `ows-api` Deployment
- `ows-worker` Deployment
- PostgreSQL via managed service or CloudNativePG
- Redis/Valkey via managed service or chart/operator
- S3-compatible object storage
- Ingress controller
- `cert-manager`
- External Secrets Operator
- OpenTelemetry Collector
- Prometheus
- Grafana
- Loki

Optional later:

- Argo CD / GitOps
- NATS JetStream when a durable internal event boundary is justified

## Managed Hosting

Managed hosting is acceptable as long as the architecture stays self-hostable.

Typical managed equivalents:

- managed Kubernetes or container platform
- managed PostgreSQL / RDS
- managed Redis/Valkey / ElastiCache
- S3
- managed secrets
- managed observability where useful

Managed hosting should be an operational choice, not a protocol dependency.

## Scaling Assumptions

The API tier must be horizontally scalable and stateless with respect to in-memory session ownership.

Assumptions:

- any API instance may receive the next checkpoint for any session
- durable history lives in PostgreSQL
- live coordination may use Redis/Valkey
- blobs live in object storage
- instances may be restarted or replaced without losing verified history

## Worker Model

The worker is the right place for later:

- report generation
- asynchronous package verification
- object-storage workflows
- expiry/degradation jobs
- optional internal event consumers

For MVP, direct API/database writes are acceptable. Do not add a worker queue just to look distributed.

## Failure Modes

Deployment docs must assume these failures happen:

- API pod dies during an active session
- worker dies during report generation
- Redis loses ephemeral state
- PostgreSQL becomes unavailable
- object storage becomes unavailable
- package upload is retried
- checkpoint is retried

Expected behavior:

- durable truth survives stateless tier failure
- ephemeral state loss does not rewrite durable history
- retries are idempotent
- trust degrades honestly when guarantees are interrupted

## Production Hardening Notes

Later hardening should include:

- autoscaling policy
- pod disruption budgets
- backup/restore drills
- database migrations discipline
- object-storage retention policy
- image signing and SBOMs
- release provenance

Do not implement all of that before the verifier API exists.
