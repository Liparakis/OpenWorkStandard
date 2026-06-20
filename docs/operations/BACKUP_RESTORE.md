# OWS Verifier Backup and Restore

This document describes what must be backed up, in what order to restore, and how to verify a successful recovery.

---

## 1. What Must Be Backed Up

A complete OWS verifier backup requires all four of the following components. **Restoring only PostgreSQL is not enough.**

| Component | What It Contains | Consequence of Loss |
|---|---|---|
| **PostgreSQL database** | Sessions, checkpoints, receipts, package metadata, verification results, audit events, API keys | Cannot look up packages, run queries, or issue new receipts |
| **Package blob volume** | Uploaded `.owspkg` files stored outside the database | Cannot re-verify or regenerate reports for existing packages |
| **Signing key material** | `VerifierStorage__ReceiptSigningKey` value | Old receipts become unverifiable; new receipts use a different key fingerprint |
| **Deployment configuration** | `.env` file (or equivalent secrets) excluding raw secrets | Cannot reconstruct the stack without connection strings and signing key references |

> [!IMPORTANT]
> Package blobs live outside PostgreSQL in a named Docker volume (`ows-verifier-package-data`). Backing up only the database leaves package metadata without the bytes needed for re-verification or report regeneration.

---

## 2. Backup Procedures

### 2.1 PostgreSQL Database

Use `pg_dump` to export a consistent snapshot:

```bash
# Dump to a timestamped SQL file
docker compose -f deploy/compose/docker-compose.yml exec -t postgres \
  pg_dump -U ows -d ows_verifier > ows_db_$(date +%F).sql
```

For binary format (faster restore):

```bash
docker compose -f deploy/compose/docker-compose.yml exec -t postgres \
  pg_dump -U ows -Fc -d ows_verifier > ows_db_$(date +%F).dump
```

Store the output file outside the Docker host if possible (e.g. a backup server, NAS, or encrypted cloud storage).

**Recommended frequency:** daily during active pilot programs.

### 2.2 Package Blob Volume

The blob volume (`ows-verifier-package-data`) contains all uploaded `.owspkg` files. Because blobs are content-addressed (filename = SHA-256 hash), the volume is append-only in normal operation.

```bash
# Option A: Tar the Docker volume contents
docker run --rm \
  -v ows-verifier-package-data:/data:ro \
  -v "$(pwd)":/backup \
  alpine tar czf /backup/ows_blobs_$(date +%F).tar.gz -C /data .

# Option B: rsync to a backup location (if host path is known)
# rsync -av /var/lib/docker/volumes/ows-verifier-package-data/_data/ /backup/ows-blobs/
```

**Recommended frequency:** daily. Because blobs are content-addressed, incremental backups are inherently efficient.

### 2.3 Signing Key Material

The receipt signing key is set via `VerifierStorage__ReceiptSigningKey` in the `.env` file or environment.

> [!CAUTION]
> The signing key is a secret. Do not log it, commit it, or put it in public storage. Store it in a password manager or secret management tool (e.g. HashiCorp Vault, AWS Secrets Manager, Bitwarden for pilots).

To verify you have the right key material without exposing it:

```bash
# Compare fingerprint logged at startup
docker compose logs ows-verifier | grep "Signing Key Fingerprint"
# Signing Key Fingerprint: sha256:<hex>
```

Record the fingerprint and the date range it was active. When rotating the key, record the old fingerprint and the date it was retired.

**Key custody rules:**
- The signing key should be held by no more than two operators.
- Treat it like a root credential, not a shared password.
- Do not store it in the same location as the PostgreSQL dump.

### 2.4 Deployment Configuration

Back up the `.env` file, excluding the raw secret values:

```bash
# Safe: strip lines containing keys/passwords
grep -v -E 'PASSWORD|ApiKey|SigningKey|CONNECTION' deploy/compose/.env > ows_config_$(date +%F).env.safe
```

Store the secret values separately in your password manager, referenced by variable name.

> [!WARNING]
> Never commit the `.env` file with real secrets to version control. It is in `.gitignore` for this reason.

---

## 3. Restore Order

Restore in this exact order. Skipping steps or reversing order can leave the database and blob storage inconsistent.

1. **Stop the verifier stack**
   ```bash
   docker compose -f deploy/compose/docker-compose.yml down
   ```

2. **Restore PostgreSQL**
   ```bash
   # Start only the database service
   docker compose -f deploy/compose/docker-compose.yml up -d postgres

   # Wait for postgres to be healthy, then restore
   docker compose exec -T postgres psql -U ows -d ows_verifier < ows_db_<date>.sql
   # Or for binary format:
   # docker compose exec -T postgres pg_restore -U ows -d ows_verifier ows_db_<date>.dump
   ```

3. **Restore the package blob volume**
   ```bash
   docker run --rm \
     -v ows-verifier-package-data:/data \
     -v "$(pwd)":/backup \
     alpine sh -c 'rm -rf /data/* && tar xzf /backup/ows_blobs_<date>.tar.gz -C /data'
   ```

4. **Restore signing key material**
   - Retrieve the signing key from your password manager.
   - Set it in the `.env` file under `VerifierStorage__ReceiptSigningKey`.
   - If you restored a DB backup and are using the same signing key, no action is needed.
   - If the signing key changed after the DB backup, old receipts will have a different fingerprint than the current key. Document this.

5. **Restore deployment configuration**
   - Restore the `.env` file from your safe backup.
   - Re-inject secrets from your password manager.

6. **Start the verifier**
   ```bash
   docker compose -f deploy/compose/docker-compose.yml up -d
   ```

7. **Run migrations (if upgrading)**
   ```bash
   docker compose exec ows-verifier dotnet Ows.Verifier.Server.dll migrate
   ```

8. **Check `/ready`**
   ```bash
   curl -f http://localhost:5078/ready
   ```
   Confirm `storageReady`, `educationStoreReady`, and `packageStorageReady` are all `true`.

9. **Run diagnostics summary**
   ```bash
   curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
   ```
   Confirm signing key fingerprint matches the expected value.

10. **Verify a known historical package**
    See Section 5 (Recovery Drill) for the verification step.

---

## 4. Recovery Failure Modes

| Failure Mode | Symptom | Recovery Action |
|---|---|---|
| **DB restored, blobs missing** | `/ready` shows `packageStorageReady: false` or verification returns `package.blob.missing` | Restore blob volume from backup before restarting verifier |
| **Blobs restored, DB missing** | `/ready` shows `storageReady: false`; package metadata cannot be found | Restore PostgreSQL from backup |
| **Signing key changed or lost** | Fingerprint mismatch in startup logs vs. recorded value; old receipt chain verification may fail | Restore original key material; if key is permanently lost, document the gap and treat affected sessions as unverifiable |
| **Job was Running at backup time** | Diagnostics shows `running` jobs that never complete | Wait for stale running timeout (`PackageWorkerStaleRunningTimeoutSeconds`) or restart the verifier; worker will reset jobs to `Pending` |
| **Stale Running jobs after restore** | Same as above | Restart verifier; startup recovery resets them to `Pending` automatically |
| **API keys expired or revoked** | Requests return 401 after restore | Re-create API keys using the bootstrap operator key; keys must be redistributed to reviewers |
| **DB ahead of blobs** | Metadata exists for packages whose blobs were not in the backup | Blobs uploaded after the blob backup are in the DB but not recoverable from backup. Re-upload those packages. |

---

## 5. Recovery Drill — "Restore and Verify Known Package"

Perform this drill after any restore, and optionally as a scheduled quarterly exercise.

### Before the drill

1. Identify a known verified package from before the backup:
   ```bash
   curl -H "X-OWS-Verifier-Key: <key>" http://localhost:5078/diagnostics/summary
   # Note: packageVerificationJobs.completed count
   ```
2. Record a specific package submission ID and its expected trust status:
   ```bash
   # Find recent verified packages from audit events
   curl -H "X-OWS-Verifier-Key: <key>" \
     "http://localhost:5078/audit/events?eventType=package.verified&limit=5"
   # Note: packageId, trustStatus
   ```
3. Record the signing key fingerprint:
   ```bash
   docker compose logs ows-verifier | grep "Signing Key Fingerprint"
   ```

### Stop and back up

4. Stop the stack:
   ```bash
   docker compose -f deploy/compose/docker-compose.yml down
   ```
5. Back up the database and blob volume (see Section 2).

### Restore into fresh location (optional for drill isolation)

6. (Optional) Copy the backup to a test environment or fresh volume.
7. Follow Section 3 restore steps.

### Verify recovery

8. Start the stack and confirm `/ready`.
9. Confirm signing key fingerprint matches the recorded value.
10. Query the known package status:
    ```bash
    curl -H "X-OWS-Verifier-Key: <key>" http://localhost:5078/packages/<submissionId>
    # Confirm: verificationStatus = Completed, trustStatus matches expected
    ```
11. Re-run verification to confirm the blob is intact:
    ```bash
    curl -X POST -H "X-OWS-Verifier-Key: <key>" \
      http://localhost:5078/packages/<submissionId>/verify
    # Confirm: trustStatus matches original
    ```
12. Query the verification report:
    ```bash
    curl -H "X-OWS-Verifier-Key: <key>" \
      http://localhost:5078/packages/<submissionId>/verification
    ```
13. Confirm the report trust status and assessment context match the pre-backup state.

### Drill pass criteria

- `/ready` returns 200 with all dependencies true
- Signing key fingerprint matches
- Package status is `Completed`
- Re-verification produces the same trust status
- Report can be read without error

> [!NOTE]
> If trust status differs after restore (e.g. `Verified` becomes `Degraded`), check whether the signing key changed. A different signing key will cause receipt chain verification to fail for old receipts.
