# Troubleshooting Guide

This guide contains a troubleshooting matrix to resolve common configuration and environment issues encountered during OWS setup or verification.

---

## Troubleshooting Matrix

| Symptom | Likely Cause | How to Check | Fix |
| :--- | :--- | :--- | :--- |
| **PowerShell script execution blocked** | Restricted system Execution Policy. | Run `Get-ExecutionPolicy` in PowerShell. If it returns `Restricted`, script execution is blocked. | Run the script with bypass policy:<br>`powershell -ExecutionPolicy Bypass -File .\scripts\windows\<script>.ps1` |
| **"Unauthorized (401)" errors on server-side requests** | The verifier server has the API key guard enabled, but the client did not send it or sent a wrong key. | Check if `VerifierSecurity__ApiKey` is set on the verifier server. Check if `OWS_VERIFIER_API_KEY` is set in your client shell. | Set matching values in both environments:<br>- Server: `VerifierSecurity__ApiKey="your-key"`Base<br>- Client shell: `$env:OWS_VERIFIER_API_KEY="your-key"` (Windows) or `export OWS_VERIFIER_API_KEY="your-key"` (Unix) |
| **Bearer token requests return `401` with `oidc_disabled`** | `VerifierAuth__Oidc__Enabled` is still false, so JWT bearer auth is not active. | Check `/ready` or `/diagnostics/summary` for the safe `oidc.enabled` flag. | Either keep using API keys for CLI/automation or enable `VerifierAuth__Oidc__Enabled=true` and configure authority/audience/claim names. |
| **Bearer token requests return `403` with `invalid_oidc_claims`** | The token validated, but required OWS claims were missing or mapped to an unsupported role. | Check the configured `RoleClaim`, `InstitutionClaim`, and `UserIdClaim`. Compare them with the IdP token contents outside OWS logs. | Fix the IdP/app claim mapping. Institution-scoped roles require an institution claim, and `StudentClient` requires a user id claim. |
| **Request returns `400 ambiguous_authentication`** | The client sent both `X-OWS-Verifier-Key` and `Authorization: Bearer`. | Check request headers and audit events for `auth.ambiguous`. | Send exactly one credential type per request. OWS rejects dual-auth to keep RBAC and audit trails unambiguous. |
| **Verifier server fails to start with "port already in use"** | Another local service or a background verifier instance is already listening on port 5078. | Check verifier status: `.\scripts\windows\status-local-verifier.ps1`<br>Or query port bound state: `Get-Process -Id (Get-NetTCPConnection -LocalPort 5078).OwningProcess` | 1. Stop the background verifier: `.\scripts\windows\stop-local-verifier.ps1`<br>2. Or free port 5078 by terminating the conflicting process.<br>3. Or configure a custom port via `OWS_VERIFIER_BASE_URL=http://127.0.0.1:<custom-port>`. |
| **PostgreSQL connection timeout or refusal** | PostgreSQL service is not running or port 5432 is not reachable. | Run `validate-local-verifier.ps1` or check container status: `docker ps --filter name=postgres`. | 1. Start the local container: `docker compose -f docker-compose.local.yml up -d`<br>2. Or start your local system PostgreSQL service.<br>3. Verify port 5432 is open. |
| **Docker command fails with daemon error** | Docker command is present but the Docker Desktop daemon is not running. | Run `docker info`. If it fails with "error during connect", the daemon is offline. | Start the Docker Desktop application (or system service) and wait for the engine to initialize. |
| **Docker access-denied warnings appear, but verifier still starts** | The shell cannot talk to the Docker daemon, but PostgreSQL is already reachable at `localhost:5432`. | Check `.\scripts\windows\status-local-verifier.ps1` and query `http://127.0.0.1:5078/ready`. | If `/ready` is `Ready`, you can continue. Otherwise start Docker Desktop or point `OWS_VERIFIER_CONNECTION_STRING` at a reachable PostgreSQL instance. |
| **Verifier starts but `/ready` is unhealthy (503 Service Unavailable)** | PostgreSQL connection string is missing or invalid, or the DB has not initialized. | Query `http://127.0.0.1:5078/ready`. Check logs using `.\scripts\windows\logs-local-verifier.ps1 -All` to inspect database connection exceptions. | 1. Confirm `OWS_VERIFIER_CONNECTION_STRING` is set correctly.<br>2. Rerun migrations: `dotnet run --project src/Ows.Verifier.Server -- migrate`. |
| **Migration failure on startup** | Database user lacks permissions to create tables, connection parameters are mismatched, or multiple instances are all trying to auto-migrate. | Run migrations manually: `dotnet run --project src/Ows.Verifier.Server -- migrate` and read console error logs. | Ensure the PostgreSQL database exists and the configured user has full schema privileges. In multi-instance mode, run `migrate` once and set `VerifierStorage__ApplyMigrationsOnStartup=false` on steady-state instances. |
| **Package verification returns "verifier session not found"** | The package was created from a session that was not registered with the verifier, or registered under a different verifier host. | Check the package metadata session ID and verify if it exists in the verifier's database. | Make sure to start the remote session using `ows session start --server http://127.0.0.1:5078` before creating event checkpoints and compiling the package. |
| **"Report file not found" or "Report missing" error** | The verification command was not executed prior to running `ows report`. | Check if verification succeeded and produced verified outcomes. | Rerun `ows verify --server http://127.0.0.1:5078 <package>` to generate the verifier receipt history before generating the professor report. |
| **Warnings about default/dev signing keys at startup** | Verifier server is configured to run in `Production` environment mode but signing key configuration is using weak development values. | Check console startup logs for `Warning` messages, or check if the server crashed due to configuration exceptions. | Configure a secure, strong signing key (at least 16 characters) in production:<br>`VerifierStorage__ReceiptSigningKey="<secure-production-signing-key>"` |
| **Package upload returns "Uploaded package exceeds maximum size limit."** | The verifier package upload limit is lower than the submitted `.owspkg` size. | Check `VerifierStorage__MaxPackageSizeBytes` and compare it with the file size. | Raise the configured limit for your pilot deployment or submit a smaller package. |
| **Package upload returns "missing required entry" or "not a valid .owspkg archive"** | The uploaded file is not a real OWS package or is missing canonical entries. | Open the archive and confirm it contains `manifest.json`, `timeline.jsonl`, and `version_graph.json`. | Regenerate the package with `ows package` and upload that output, not a random zip file. |
| **Package status stays `Pending` or `Running`** | The in-process verification worker is disabled, crashed, or cannot reach the stored blob/package dependencies. In multi-instance mode, every instance may be API-only. | Check `PackageVerificationWorker__Enabled`, inspect `GET /diagnostics/summary` for `workerEnabled` and `instanceMode`, and read verifier logs for `package.verification.*` entries. | Re-enable the worker on exactly one worker instance, fix the storage/database issue, and restart the worker so stale running jobs can be reset to `Pending`. |
| **Multi-instance API nodes work, but verification never completes** | No worker-enabled instance is running, or the worker instance cannot see the shared package blob path. | Check `/diagnostics/summary` on all instances. Exactly one instance should report `workerEnabled: true`. Check `packageStorageReady: true` everywhere. | Start one worker-enabled instance, disable the worker on API-only instances, and mount the same shared package storage path into every instance. |
| **`/ready` or `/diagnostics/summary` warns that the worker is enabled but package storage is unavailable** | The worker instance cannot read or write the configured package blob path. | Check the `warnings` array, then verify the mounted volume/path on that instance. | Fix the shared package storage mount before accepting new uploads. Local private disks are not a valid multi-instance blob strategy. |
| **Prometheus target for `ows-verifier` is down** | The optional observability overlay is running, but the base verifier service is unavailable on the compose network or `/metrics` is unreachable. | Open Prometheus targets UI and inspect `ows-verifier`. Check that the base compose stack is running. | Start the base compose stack first, then rerun the overlay. Verify the verifier serves `/metrics`. |
| **Grafana shows no OWS dashboard** | Grafana provisioning files or dashboard mounts are missing. | Check Grafana logs and confirm `deploy/observability/grafana/provisioning/` and `deploy/observability/grafana/dashboards/` are mounted. | Fix the overlay file mounts and restart Grafana. |
| **Loki has no verifier logs** | Promtail cannot reach Docker, the verifier container name does not match, or the optional overlay is not running. | Check Promtail logs and confirm access to `/var/run/docker.sock`. Verify the verifier container name is `ows-verifier-prod`. | Restart Promtail, fix Docker socket access, or update the Promtail container filter if the verifier container name changes. |
| **Grafana dashboard has no request count or duration panels** | OWS v0.1 does not expose request count/duration metrics on `/metrics`. | Inspect the dashboard and `docs/operations/OBSERVABILITY.md`. | This is expected. Use verifier logs plus `X-Request-Id` correlation until request metrics are added later. |
| **Package verification fails with "Package blob not found"** | Metadata still exists, but the blob volume or file was removed. | Check the package storage volume and compare the package record with the local blob directory contents. Check `/diagnostics/summary` for `packageBlobCount`. | Restore the blob volume from backup or re-upload the package. PostgreSQL metadata alone is not enough. See `BACKUP_RESTORE.md`. |
| **After a restore, trust status differs from expected (e.g. `Verified` → `Degraded`)** | The signing key changed between the backup and the restore, so old receipts fail chain verification. | Compare the signing key fingerprint in startup logs with the recorded pre-backup fingerprint. | Restore the original signing key material (`VerifierStorage__ReceiptSigningKey`). See `SECURITY_HARDENING.md` for signing key custody procedures. |
| **`/diagnostics/summary` shows `signingKeyFingerprintPresent: false`** | The signing key is not configured or is empty. | Check that `VerifierStorage__ReceiptSigningKey` is set in the `.env` file and is at least 16 characters. | Set a strong signing key and restart the verifier. |
| **`/diagnostics/summary` shows `packageBlobCount: null`** | The package blob storage root is not accessible or not configured. | Check `/ready` for `packageStorageReady`. Check the Docker volume mount and `VerifierStorage__LocalStoragePath`. | Ensure the `verifier-packages` volume is mounted correctly. Restart the verifier. |
| **Warnings about non-administrator shell** | Not a failure. System warning indicating shell is not elevated. | None. | Administrator privileges are **not required** to run OWS watcher, CLI, or local verifier services. You can safely ignore this check. |

---

## Student-Facing Troubleshooting & Recovery

### 1. Common Student Status Indicators
- **WatchingLocalOnly**: The file watcher is running locally, but no remote verifier session is active (or verifier URL is omitted). Work is saved only on your local machine.
- **SessionActive**: A remote verifier session is registered. Files are tracked, and heartbeats/checkpoints are successfully sent. (Standard mode for active exams/assignments).
- **VerifierOffline**: The remote verifier is currently unreachable (network down or server crashed). OWS continues local tracking; it will automatically retry connecting when network returns.
- **HeartbeatFailing**: The verifier returned an error during a heartbeat (such as `401 Unauthorized` or `403 Forbidden`). This usually indicates an expired or misconfigured API key.
- **Degraded**: A lease gap was detected by the verifier (e.g., your laptop was closed, or heartbeats were paused for a long time). OWS remains active, but you should continue working so continuity can be verified.
- **Error**: The watcher process crashed or could not start (such as when the CLI executable path is incorrect).

### 2. How to Recover from a Stale Watcher
If you see an error saying the watcher is already running, or if the watcher crashes:
1. Run `ows watch stop` in the terminal, or select `OWS: Stop Watch Session` in VS Code.
2. If it still fails, OWS will automatically recover on the next start by checking if the process ID (PID) stored in `.ows/watcher.json` belongs to an active OWS/dotnet process. If the process is dead, OWS safely deletes the stale lock file.
3. You can also manually delete `.ows/watcher.json` and `.ows/watcher.stop` if you need to force a reset.

### 3. Reconfiguring Assessment Context
If you entered the wrong Student ID, Assessment ID, or Verifier URL:
- Run `OWS: Configure Assessment Context` in VS Code to enter new values.
- Or run `ows init` again, then edit `.ows/config.json` manually.

### 4. Safely Resetting Local State
If your local state becomes corrupt or you want to start fresh:
1. Stop the watcher: `ows watch stop`.
2. Delete the `.ows` folder from your project root. (Caution: this removes local timeline history and receipts).
3. Initialize the project again: `ows init`.

### 5. How to Report an Issue
If you run into an error you cannot solve, please contact your instructor/operator and provide:
- **Request ID**: Find the `X-Request-Id` in the verifier logs or CLI outputs.
- **Session ID**: Located in `.ows/session.json` or displayed in `ows status`.
- **Package ID / Submission ID**: Returned after running `ows package upload`.

---

## See Also

- [PILOT_DEMO.md](../workflows/PILOT_DEMO.md) - end-to-end pilot validation walkthrough
- [OPERATIONS_RUNBOOK.md](OPERATIONS_RUNBOOK.md) — daily operator procedures and emergency runbook
- [BACKUP_RESTORE.md](BACKUP_RESTORE.md) — what to back up, restore order, recovery drills
- [SECURITY_HARDENING.md](SECURITY_HARDENING.md) — signing key custody, API key management, diagnostics fields
- [SELF_HOSTED_COMPOSE.md](SELF_HOSTED_COMPOSE.md) — production Docker Compose deployment

When OWS package verification fails or returns `Invalid`/`Unverified` status, inspect the findings in the generated review reports. Reference this table:

| Finding Code | Severity | Description / Cause | Suggested Fix |
| :--- | :--- | :--- | :--- |
| `timeline.chain.broken` | Critical | The timeline sequence has event hash mismatches or is out-of-order. | Request a package resubmission. The local events chain is corrupted. |
| `package.hash.invalid` | Critical | The SHA-256 hashes of one or more packaged files do not match the manifest. | The package has been modified or corrupted post-generation. Re-run `ows package` and verify again. |
| `receipt.chain.missing` | Medium | The package contains no verifier notarization receipts. | If remote verification was expected, verify if the session was started with the `--server` flag. |
| `package.anchor.missing` | Medium | The package is not anchored to a registered verifier session head. | Verify that the session was synchronized with the verifier during work. |
| `verifier.session.head.mismatch` | High | The local timeline head does not match the verifier session head. | Check timeline sync logs; indicates a mismatch between local history and verifier-notarized state. |
| `lease.gap.long` | High | The session heartbeat was interrupted for an interval exceeding the significance threshold. | Conduct a thorough manual review of code changes around the gap interval. |
| `lease.work_after_expiration` | High | Timeline events were recorded after the remote verifier session lease expired. | Verify whether the user forgot to run heartbeats or if the session was kept open past expiration. |

