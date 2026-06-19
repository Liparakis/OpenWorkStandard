# Troubleshooting Guide

This guide contains a troubleshooting matrix to resolve common configuration and environment issues encountered during OWS setup or verification.

---

## Troubleshooting Matrix

| Symptom | Likely Cause | How to Check | Fix |
| :--- | :--- | :--- | :--- |
| **PowerShell script execution blocked** | Restricted system Execution Policy. | Run `Get-ExecutionPolicy` in PowerShell. If it returns `Restricted`, script execution is blocked. | Run the script with bypass policy:<br>`powershell -ExecutionPolicy Bypass -File .\scripts\<script>.ps1` |
| **"Unauthorized (401)" errors on server-side requests** | The verifier server has the API key guard enabled, but the client did not send it or sent a wrong key. | Check if `VerifierSecurity__ApiKey` is set on the verifier server. Check if `OWS_VERIFIER_API_KEY` is set in your client shell. | Set matching values in both environments:<br>- Server: `VerifierSecurity__ApiKey="your-key"`Base<br>- Client shell: `$env:OWS_VERIFIER_API_KEY="your-key"` (Windows) or `export OWS_VERIFIER_API_KEY="your-key"` (Unix) |
| **Verifier server fails to start with "port already in use"** | Another local service or a background verifier instance is already listening on port 5078. | Check verifier status: `.\scripts\status-local-verifier.ps1`<br>Or query port bound state: `Get-Process -Id (Get-NetTCPConnection -LocalPort 5078).OwningProcess` | 1. Stop the background verifier: `.\scripts\stop-local-verifier.ps1`<br>2. Or free port 5078 by terminating the conflicting process.<br>3. Or configure a custom port via `OWS_VERIFIER_BASE_URL=http://127.0.0.1:<custom-port>`. |
| **PostgreSQL connection timeout or refusal** | PostgreSQL service is not running or port 5432 is not reachable. | Run `validate-local-verifier.ps1` or check container status: `docker ps --filter name=postgres`. | 1. Start the local container: `docker compose -f docker-compose.local.yml up -d`<br>2. Or start your local system PostgreSQL service.<br>3. Verify port 5432 is open. |
| **Docker command fails with daemon error** | Docker command is present but the Docker Desktop daemon is not running. | Run `docker info`. If it fails with "error during connect", the daemon is offline. | Start the Docker Desktop application (or system service) and wait for the engine to initialize. |
| **Verifier starts but `/ready` is unhealthy (503 Service Unavailable)** | PostgreSQL connection string is missing or invalid, or the DB has not initialized. | Query `http://127.0.0.1:5078/ready`. Check logs using `.\scripts\logs-local-verifier.ps1 -All` to inspect database connection exceptions. | 1. Confirm `OWS_VERIFIER_CONNECTION_STRING` is set correctly.<br>2. Rerun migrations: `dotnet run --project src/Ows.Verifier.Server -- migrate`. |
| **Migration failure on startup** | Database user lacks permissions to create tables, or connection parameters are mismatched. | Run migrations manually: `dotnet run --project src/Ows.Verifier.Server -- migrate` and read console error logs. | Ensure the PostgreSQL database exists and the configured user has full schema privileges. Use local default connection parameters where possible. |
| **Package verification returns "verifier session not found"** | The package was created from a session that was not registered with the verifier, or registered under a different verifier host. | Check the package metadata session ID and verify if it exists in the verifier's database. | Make sure to start the remote session using `ows session start --server http://127.0.0.1:5078` before creating event checkpoints and compiling the package. |
| **"Report file not found" or "Report missing" error** | The verification command was not executed prior to running `ows report`. | Check if verification succeeded and produced verified outcomes. | Rerun `ows verify --server http://127.0.0.1:5078 <package>` to generate the verifier receipt history before generating the professor report. |
| **Warnings about default/dev signing keys at startup** | Verifier server is configured to run in `Production` environment mode but signing key configuration is using weak development values. | Check console startup logs for `Warning` messages, or check if the server crashed due to configuration exceptions. | Configure a secure, strong signing key (at least 16 characters) in production:<br>`VerifierSecurity__SigningKey="<secure-production-signing-key>"` |
| **Warnings about non-administrator shell** | Not a failure. System warning indicating shell is not elevated. | None. | Administrator privileges are **not required** to run OWS watcher, CLI, or local verifier services. You can safely ignore this check. |

---

## Common Verification Error Codes

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
