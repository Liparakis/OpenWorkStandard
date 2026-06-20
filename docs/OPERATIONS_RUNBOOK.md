# OWS Verifier Operations Runbook

This runbook covers day-to-day and emergency procedures for operating a self-hosted Open Work Standard verifier during a pilot program.

## 1. Quick-Start Checks

Run these every time you start or restart the stack:

```bash
docker compose -f deploy/compose/docker-compose.yml ps
curl -f http://localhost:5078/health
curl -f http://localhost:5078/ready
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

Expected `/ready` response shape:

```json
{
  "status": "Ready",
  "storage": "postgres",
  "storageProvider": "postgres",
  "packageStorageProvider": "local-file",
  "workerEnabled": true,
  "instanceMode": "api+worker",
  "applyMigrationsOnStartup": true,
  "signing": "Enabled",
  "warnings": [],
  "dependencies": {
    "storageProvider": "postgres",
    "storageReady": true,
    "educationStoreReady": true,
    "packageStorageConfigured": true,
    "packageStorageReady": true,
    "workerEnabled": true,
    "instanceMode": "api+worker",
    "signingConfigured": true,
    "authMode": "persisted",
    "oidc": {
      "enabled": false,
      "authorityConfigured": false,
      "audienceConfigured": false,
      "roleClaimConfigured": true
    }
  }
}
```

If `/ready` returns 503, stop and fix the deployment before accepting packages.

Optional external observability is layered on top and is not required for these checks.

## 2. Startup Checklist

1. Start the stack: `docker compose up -d`
2. Wait for PostgreSQL to become healthy: `docker compose ps`
3. If this is a new deployment or upgrade, run migrations once: `docker compose exec ows-verifier dotnet Ows.Verifier.Server.dll migrate`
4. In multi-instance mode, confirm every steady-state instance has `VerifierStorage__ApplyMigrationsOnStartup=false`
5. Confirm `/health` returns HTTP 200
6. Confirm `/ready` returns HTTP 200 and the expected `instanceMode`
7. Confirm `/diagnostics/summary` reports `packageStorageReady: true`, correct `workerEnabled`, and `signingKeyFingerprintPresent: true`
8. If OIDC/JWT bearer is enabled, confirm `/ready` or `/diagnostics/summary` shows the expected safe OIDC status and that no secrets are exposed
9. Record the signing key fingerprint from startup logs

If you want external dashboards and log search during a pilot, start the overlay separately:

```bash
docker compose -f deploy/compose/docker-compose.yml -f deploy/compose/docker-compose.observability.yml up -d
```

## 3. Signing Key Fingerprint

The verifier logs the signing key fingerprint at startup:

```text
OWS Verifier starting up...
Signing Key Fingerprint: sha256:<hex>
```

Retrieve it later with:

```bash
docker compose logs ows-verifier | grep "Signing Key Fingerprint"
```

Record the fingerprint in operator notes. A changed fingerprint means the signing key changed.

## 4. Package Blob Storage

Package blobs are stored outside PostgreSQL. PostgreSQL holds metadata and job state; blob storage holds the actual `.owspkg` bytes. Back up both together.

Current provider:

- `local-file`

Current rule:

- single-node mode may use one local verifier volume
- multi-instance mode must mount one shared package storage path into every instance that needs package reads or verification

Blob count check:

```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

Look at `packageBlobCount`.

## 5. Package Verification Worker

The in-process worker claims `Pending` jobs and processes them against stored blobs.

Worker configuration:

- `PackageVerificationWorker__Enabled`
- `VerifierStorage__PackageWorkerPollIntervalMilliseconds`
- `VerifierStorage__PackageWorkerStaleRunningTimeoutSeconds`

Multi-instance pilot rule:

- exactly one instance should have `PackageVerificationWorker__Enabled=true`
- API-only instances should report `instanceMode: api-only`
- the worker instance should report `instanceMode: api+worker`
- all instances must see the same shared package storage

PostgreSQL job claiming already uses row locking with `FOR UPDATE SKIP LOCKED`, so accidental double workers should not normally claim the same pending job.

Check job state via diagnostics:

```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

Look for:

- `workerEnabled`
- `instanceMode`
- `warnings`
- `packageVerificationJobs.pending`
- `packageVerificationJobs.running`
- `packageVerificationJobs.succeeded`
- `packageVerificationJobs.failed`

If jobs accumulate in `pending`, check:

1. One instance really has `workerEnabled: true`
2. `packageStorageReady: true` everywhere
3. Worker logs do not show `package.verification.*` failures

## 6. Migration Safety

Single-node local/dev deployments may leave automatic migrations enabled.

Multi-instance pilot deployments should not.

Recommended sequence:

1. Stop or hold new traffic.
2. Run `dotnet Ows.Verifier.Server.dll migrate` once against the shared PostgreSQL database.
3. Start API and worker instances with `VerifierStorage__ApplyMigrationsOnStartup=false`.
4. Verify `/ready` and `/diagnostics/summary` on each instance.

## 7. Optional External Observability

Optional stack:

- Prometheus
- Grafana
- Loki
- Promtail

It is not required for base self-hosting.

Use it when operators want:

- a dashboard instead of raw metrics text
- longer-lived pilot log search
- easier review of readiness and verification-job trends

See `docs/OBSERVABILITY.md` for startup and troubleshooting details.

Do not try to invent a migration coordinator at this stage.

## 7. Auth Operations

API keys remain the primary pilot mechanism for CLI, VS Code, watchers, and automation.

Optional OIDC/JWT bearer notes:

- enable it only when you need future human-facing API access patterns
- do not call it full SSO
- do not send both API key and bearer token on one request
- dual-auth requests are rejected with `400` and audited as `auth.ambiguous`

## 8. API Key Management

Daily operations summary:

| Action | Endpoint | Scope |
|---|---|---|
| Create operator key | `POST /auth/api-keys` | Operator only |
| Create institution admin key | `POST /auth/api-keys` | Operator only |
| Create reviewer key | `POST /auth/api-keys` | Operator or institution admin |
| List keys | `GET /auth/api-keys` | Operator only |
| Revoke key | `POST /auth/api-keys/{id}/revoke` | Operator only |

Raw key secrets are returned once and never stored.

## 9. Audit Events

Useful audit queries:

```bash
curl -H "X-OWS-Verifier-Key: <key>" http://localhost:5078/audit/events?limit=50
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?eventType=package.verified&limit=20"
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?packageId=<submissionId>"
```

Key event types:

| Event | Meaning |
|---|---|
| `package.submitted` | Package blob uploaded |
| `package.verification.queued` | Verification job queued |
| `package.verification.started` | Worker claimed the job |
| `package.verification.completed` | Verification job finished |
| `package.verification.failed` | Verification job failed |
| `package.blob.missing` | Blob missing at verification time |
| `readiness.failed` | `/ready` returned unhealthy |
| `auth.ambiguous` | Request sent both API key and bearer token |

## 10. Emergency Procedures

### All jobs stuck in pending

1. Check `workerEnabled` and `instanceMode` in `/diagnostics/summary`
2. Check `/ready` for `packageStorageReady: true`
3. Check `warnings` for package-storage problems
4. Restart the worker instance

### Worker enabled but package storage unavailable

1. Check the `warnings` array in `/ready` or `/diagnostics/summary`
2. Verify the shared package volume/path is mounted on that instance
3. Do not accept new uploads until storage is fixed

### Grafana or Loki exposed too broadly

1. Stop treating the overlay as internal-only
2. Put Grafana and Loki behind operator-managed auth and a reverse proxy
3. Review logs and dashboards for overshared operational context

### Package blob missing

1. Re-upload the package or restore the blob volume from backup
2. Rerun verification: `POST /packages/{id}/verify`

### Database unreachable

1. Check `docker compose ps`
2. Check PostgreSQL connectivity from the verifier container
3. Restart PostgreSQL if the data is healthy
4. If the data may be corrupt, follow `BACKUP_RESTORE.md`

## 10. Scheduled Tasks

| Frequency | Task |
|---|---|
| Daily | Check `/ready` and `/diagnostics/summary` |
| Daily | Scan audit events for `auth.failed` spikes |
| Weekly | Back up PostgreSQL database |
| Weekly | Back up package blob storage |
| Before each pilot | Record the signing key fingerprint |
| After upgrades | Run `migrate`, then verify `/ready` and `/diagnostics/summary` |
