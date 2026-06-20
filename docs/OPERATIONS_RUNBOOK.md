# OWS Verifier Operations Runbook

This runbook covers day-to-day and emergency procedures for operating a self-hosted OWS verifier during a pilot program. It is a companion to `BACKUP_RESTORE.md`, `SECURITY_HARDENING.md`, and `SELF_HOSTED_COMPOSE.md`.

---

## 1. Quick-Start Checks

Run these every time you start or restart the stack:

```bash
# 1. Verify containers are up
docker compose -f deploy/compose/docker-compose.yml ps

# 2. Confirm health
curl -f http://localhost:5078/health

# 3. Confirm readiness (storage + signing + auth mode)
curl -f http://localhost:5078/ready

# 4. Read diagnostics summary (requires operator key)
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

Expected `/ready` response shape:

```json
{
  "status": "Ready",
  "storage": "postgres",
  "signing": "Enabled",
  "dependencies": {
    "storageProvider": "postgres",
    "storageReady": true,
    "educationStoreReady": true,
    "packageStorageReady": true,
    "signingConfigured": true,
    "authMode": "persisted"
  }
}
```

If `/ready` returns 503, stop and fix before accepting student packages.

---

## 2. Startup Checklist (After Deploy or Restart)

1. `docker compose up -d` (or equivalent restart)
2. Wait for `ows-postgres-prod` to be healthy (`docker compose ps`)
3. Run migrations if this is an upgrade: `docker compose exec ows-verifier dotnet Ows.Verifier.Server.dll migrate`
4. Call `/health` — confirm 200
5. Call `/ready` — confirm 200 and all dependencies true
6. Call `/diagnostics/summary` — confirm `packageStorageReady: true` and `signingKeyFingerprintPresent: true`
7. Log the signing key fingerprint from startup logs: `docker compose logs ows-verifier | grep Fingerprint`

---

## 3. Signing Key Fingerprint

The verifier logs the signing key fingerprint at startup:

```
OWS Verifier starting up...
Signing Key Fingerprint: sha256:<hex>
```

To retrieve it after start:

```bash
docker compose logs ows-verifier | grep "Signing Key Fingerprint"
```

The fingerprint is a safe, non-secret identifier for the key material in use. Record it in your operator notes and compare it after any config change or restore. A mismatch indicates the signing key changed.

> [!IMPORTANT]
> If the fingerprint changes between deployments, receipts signed under the old key will still verify correctly only against that old fingerprint. Document the old fingerprint alongside the date range it was active.

---

## 4. Package Blob Storage

Package blobs (`.owspkg` files) are stored in the named Docker volume `ows-verifier-package-data`, mounted at `/app/packages` by default.

The PostgreSQL database holds submission metadata and verification results. The blob volume holds the actual package bytes. **Both must be backed up together.** Restoring only the database leaves metadata without the package bytes needed for re-verification.

Blob naming convention: `<sha256-hash>.owspkg` — content-addressed, so deduplication is implicit.

### Check blob count

```bash
# List blob count inside the container
docker compose exec ows-verifier sh -c 'ls /app/packages/*.owspkg 2>/dev/null | wc -l'
```

Or via the diagnostics summary:

```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
# packageBlobCount field (if non-zero)
```

---

## 5. Package Verification Worker

The in-process worker picks up `Pending` jobs and runs them against stored blobs. Worker state is controlled by:

- `VerifierStorage__PackageWorkerEnabled` (true/false)
- `VerifierStorage__PackageWorkerPollIntervalMilliseconds`
- `VerifierStorage__PackageWorkerStaleRunningTimeoutSeconds`

On restart, any jobs that were `Running` and have not timed out are reset to `Pending` automatically. This is startup recovery — it is expected behavior and not an error.

Check job state via diagnostics:

```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

Look for `packageVerificationJobs.pending`, `.running`, `.completed`, `.failed`.

If jobs accumulate in `pending` and nothing moves to `completed`, check:

1. Worker is enabled (`PackageWorkerEnabled: true` in logs)
2. Package blob volume is accessible (`packageStorageReady: true` in `/ready`)
3. No unhandled exception in container logs

---

## 6. API Key Management

See `SECURITY_HARDENING.md` for full key lifecycle documentation.

Daily operations summary:

| Action | Endpoint | Scoping & Delegation |
|---|---|---|
| Create operator key | `POST /auth/api-keys` with `{"role":"Operator"}` | Operator only |
| Create institution admin key | `POST /auth/api-keys` with `{"role":"InstitutionAdmin", "institutionId":"<id>"}` | Operator only |
| Create reviewer key | `POST /auth/api-keys` with `{"role":"InstructorReviewer", "institutionId":"<id>"}` | Operator, or InstitutionAdmin (own institution only) |
| List keys | `GET /auth/api-keys` | Operator only |
| Revoke key | `POST /auth/api-keys/{id}/revoke` | Operator only |

Raw key secrets are returned once on creation and never stored. Store them in a password manager immediately.

---

## 7. Audit Events

Query recent audit events to understand what the verifier has been doing:

```bash
# All recent events
curl -H "X-OWS-Verifier-Key: <key>" http://localhost:5078/audit/events?limit=50

# Filter by type
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?eventType=package.verified&limit=20"

# Filter by package
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?packageId=<submissionId>"

# Filter by institution
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?institutionId=<id>"
```

Key event types:

| Event | Meaning |
|---|---|
| `session.created` | New student assessment session started |
| `checkpoint.accepted` | Timeline checkpoint recorded |
| `heartbeat.accepted` | Session heartbeat received |
| `lease.gap.detected` | Heartbeat gap — may affect trust grade |
| `package.submitted` | Package blob uploaded |
| `package.verified` | Verification job completed |
| `package.blob.missing` | Blob was missing at verification time |
| `api_key.created` | New API key created |
| `api_key.revoked` | Key revoked |
| `auth.failed` | Invalid or missing key |
| `readiness.failed` | `/ready` returned unhealthy |

---

## 8. Requesting Correlation

Every request to the verifier includes an `X-Request-Id` response header. Use it to:

1. Match the request in container logs
2. Query `GET /audit/events` filtered to that request
3. Pinpoint which operation triggered an error

Example:

```bash
# After an upload, note the X-Request-Id header
curl -v -H "X-OWS-Verifier-Key: <key>" -F file=@mypackage.owspkg http://localhost:5078/packages/upload
# < X-Request-Id: abc123

# Find related audit events
curl -H "X-OWS-Verifier-Key: <key>" "http://localhost:5078/audit/events?requestId=abc123"
```

---

## 9. Emergency Procedures

### Signing key warning at startup

If startup logs show `CONFIGURATION WARNING: VerifierStorage:ReceiptSigningKey is weak`, the verifier is running without production-grade signing. In production mode this is fatal. In development mode it degrades trust in all receipts. Rotate the key before accepting real student submissions.

### Package blob missing

If verification returns `package.blob.missing`, the blob was accepted but later lost. Actions:

1. Ask the student or operator to re-upload the package
2. Or restore the blob volume from backup
3. After restoring, rerun verification: `POST /packages/{id}/verify`
4. Do not forge or substitute blobs — hash mismatch will produce `Invalid`

### Stale running jobs after restore

After a restore, jobs that were `Running` at backup time will be found in `Running` state. The startup recovery mechanism resets them to `Pending` after `PackageWorkerStaleRunningTimeoutSeconds`. If you need them reset immediately, restart the verifier service.

### All jobs stuck in pending

1. Check `VerifierStorage__PackageWorkerEnabled` is `true`
2. Check `/ready` for `packageStorageReady: true`
3. Check container logs for exceptions from the worker
4. Restart the verifier container

### Database unreachable

1. Check PostgreSQL container is running: `docker compose ps`
2. Check connectivity: `docker compose exec ows-verifier sh -c 'pg_isready -h postgres -U ows'`
3. Restart PostgreSQL if healthy data is present: `docker compose restart postgres`
4. If data may be corrupt, follow restore procedures in `BACKUP_RESTORE.md`

---

## 10. Scheduled Operator Tasks

| Frequency | Task |
|---|---|
| Daily | Check `/ready` and `/diagnostics/summary` |
| Daily | Scan audit events for `auth.failed` spikes |
| Weekly | Back up PostgreSQL database |
| Weekly | Back up package blob volume |
| Before each pilot | Record signing key fingerprint |
| Before each pilot | Verify API keys for reviewers are valid and scoped correctly |
| After each pilot | Archive audit events and diagnostics summary |
| After upgrades | Re-run migrations, verify `/ready`, check signing key fingerprint |
