# Local Durable Verifier

## Goal

Exercise the Work Verifier against real PostgreSQL in local development.

This is the smallest useful deployment-validation path for the current MVP.

## Maintenance Rule

If the verifier workflow changes, update `docs/development/ROADMAP_CHECKLIST.md` and this document in the same commit.

## Clean Clone Quick Start

From a clean clone:

1. run `dotnet build OWS.sln -nologo`
2. start PostgreSQL with `docker compose -f docker-compose.local.yml up -d`
3. run `.\scripts\start-local-verifier.ps1`
4. confirm `.\scripts\status-local-verifier.ps1`
5. run `.\scripts\test-local-verifier.ps1`
6. inspect `.\scripts\logs-local-verifier.ps1 -All` if anything fails

On Unix-like environments, use the matching `.sh` scripts from `scripts/` or `artifacts/generated-scripts/`.

## Local PostgreSQL

Use the local compose file:

```bash
docker compose -f docker-compose.local.yml up -d
```

Or use the one-command local runner:

```powershell
.\scripts\run-local-verifier.ps1
```

After `dotnet build`, platform-specific launcher artifacts are also emitted to:

- `artifacts/generated-scripts/`

For background lifecycle on Windows:

```powershell
.\scripts\doctor-local-verifier.ps1
.\scripts\start-local-verifier.ps1
.\scripts\status-local-verifier.ps1
.\scripts\logs-local-verifier.ps1
.\scripts\test-local-verifier.ps1
.\scripts\stop-local-verifier.ps1
```

For Unix-like environments:

```bash
./scripts/doctor-local-verifier.sh
./scripts/start-local-verifier.sh
./scripts/status-local-verifier.sh
./scripts/logs-local-verifier.sh
./scripts/test-local-verifier.sh
./scripts/stop-local-verifier.sh
```

If the verifier DLL has not been built yet, run:

```powershell
dotnet build OWS.sln -nologo
```

The `run-local-verifier` and `start-local-verifier` helpers now do this automatically when the verifier build output is missing.

Host storage path on this machine:

- `D:\Containers\OWS\postgres\data`

Default local database settings:

- database: `ows_verifier`
- user: `ows`
- password: `ows-dev`
- port: `5432`

## Run Verifier Migrations

Set the verifier storage provider and connection string, then run:

```bash
dotnet run --project src/Ows.Verifier.Server -- migrate
```

The local runner already does this for you.

Recommended environment variables:

```powershell
$env:VerifierStorage__Provider = "postgres"
$env:VerifierStorage__PostgresConnectionString = "Host=localhost;Port=5432;Database=ows_verifier;Username=ows;Password=ows-dev"
```

Optional local API key guard:

```powershell
$env:VerifierSecurity__ApiKey = "dev-api-key"
$env:OWS_VERIFIER_API_KEY = "dev-api-key"
```

When configured, the verifier requires `X-OWS-Verifier-Key`; the CLI sends that header from `OWS_VERIFIER_API_KEY`.

## Run Verifier Server

With the same environment variables:

```bash
dotnet run --project src/Ows.Verifier.Server
```

The local runner starts the verifier on `http://127.0.0.1:5078`.

## Sanity Check

Basic local flow:

1. start PostgreSQL
2. run verifier migrations
3. run verifier server
4. run `ows session start --server http://127.0.0.1:5078`
5. run `ows session checkpoint`
6. run `ows verify --server http://127.0.0.1:5078 <package>`

Or run the direct verifier smoke script:

```powershell
.\scripts\test-local-verifier.ps1
```

On non-Windows builds, use the generated `.sh` variants from `artifacts/generated-scripts/`.

That script checks:

- session creation
- first checkpoint append
- idempotent retry behavior
- receipt-chain fetch
- head fetch
- multipart package upload
- asynchronous package verification completion
- package report retrieval

Use `validate-local-verifier.ps1` (or `validate-local-verifier.sh` on Unix/macOS) when you want a comprehensive read-only environmental preflight check before starting anything. It diagnostics-checks .NET SDK, Docker, PostgreSQL reachability, verifier port binds, health/readiness endpoints, API keys, paths containing spaces, PowerShell Execution Policies, and non-admin shell warnings.

Refer to [LOCAL_DEMO.md](../workflows/LOCAL_DEMO.md) for a detailed, step-by-step local validation guide, and [TROUBLESHOOTING.md](../operations/TROUBLESHOOTING.md) for a complete troubleshooting diagnostics matrix.


## Environment Modes and Production Hardening

The verifier server supports three environment modes configured via the `VerifierEnvironment` configuration key (or falling back to the standard `ASPNETCORE_ENVIRONMENT` / `builder.Environment.EnvironmentName` if not set):

- `Development`
- `Local`
- `Production`

### Production Enforcement

When starting in `Production` mode, the verifier will execute strict validation checks and immediately abort startup (throwing a fatal configuration exception) if any insecure configuration is detected:
1. **JSON Storage Provider Disabled**: In production, the `json` storage provider is rejected; the verifier must use the `postgres` provider.
2. **API Key Requirement**: The verifier API guard must be enabled with a strong key (at least 16 characters, not a known default dev-key).
3. **Receipt Signing Key Requirement**: Receipts must be signed with a strong key (at least 16 characters, not a known default).

In `Development` or `Local` modes, insecure configurations (such as using the JSON file provider or default/weak keys) will log high-visibility warning logs but allow startup to proceed.

### Health and Readiness Endpoints

The verifier exposes the following observability endpoints:
- `GET /health`: Returns HTTP 200 OK and `{ "status": "Healthy" }` to indicate the web server is running.
- `GET /ready`: Evaluates backend dependency status. Checks storage provider health (by running `select 1;` for PostgreSQL or verifying directories are writable for JSON storage) and verifies if receipt signing is enabled. If storage check fails, returns HTTP 503 Service Unavailable.

## Troubleshooting

### PowerShell execution policy

If PowerShell blocks script execution, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-local-verifier.ps1
```

The background start helper already launches its child PowerShell process with `-ExecutionPolicy Bypass`.

### Port conflicts

If verifier startup says the port is already in use:

- run `.\scripts\status-local-verifier.ps1`
- stop the managed verifier with `.\scripts\stop-local-verifier.ps1` if it is yours
- otherwise free `127.0.0.1:5078` or set `OWS_VERIFIER_BASE_URL` before running the helpers

### Missing Postgres

If PostgreSQL is unavailable:

- confirm Docker can start `docker-compose.local.yml`
- confirm port `5432` is reachable locally
- confirm the local container data path is still `D:\Containers\OWS\postgres\data`

### Failed migrations

If migrations fail:

- verify PostgreSQL is reachable
- verify `OWS_VERIFIER_CONNECTION_STRING` if you overrode the default
- run `.\scripts\logs-local-verifier.ps1 -All` to see the startup failure without losing console output
- rerun the startup helper after fixing the database issue

### Package upload or verification failures

If package intake fails:

- confirm `VerifierStorage__LocalStoragePath` points to a writable directory or mounted volume
- confirm `VerifierStorage__MaxPackageSizeBytes` is large enough for the test package
- inspect `.\scripts\logs-local-verifier.ps1 -All` for `package.upload.*` or `package.verification.*` events
- query `GET /diagnostics/summary` with an operator key to confirm whether jobs are stuck in `Pending`, `Running`, or `Failed`
- if the verifier crashed mid-job, restart it and let stale `Running` jobs be reset to `Pending`

### Missing verifier API key

If verifier requests return `401`:

- confirm `VerifierSecurity__ApiKey` is set on the verifier only when you intend to guard it
- set matching `OWS_VERIFIER_API_KEY` before running CLI commands
- do not put the API key in `.ows/session.json` or packages

### Stale PID files

If status reports `stale_pid` or `crashed`:

- run `.\scripts\stop-local-verifier.ps1`
- rerun `.\scripts\start-local-verifier.ps1`

If status reports `unreachable`:

- run `.\scripts\logs-local-verifier.ps1 -All`
- verify the configured base URL and port
- verify another local process is not intercepting the port

### Paths with spaces

The helpers quote repo and artifact paths. Run them as normal from repositories whose paths contain spaces.

### Reading verifier logs

Use:

```powershell
.\scripts\logs-local-verifier.ps1
.\scripts\logs-local-verifier.ps1 -All
```

The log paths are also shown by `status-local-verifier.ps1`.

### Clean clone build failures

If the helper scripts fail because the verifier DLL is missing:

- rerun `start-local-verifier` or `run-local-verifier` once and let it auto-build
- or run `dotnet build OWS.sln -nologo`
- rerun the helper command

## Scope

This is only for local durable-store validation.

It is not yet:

- production hardening
- multi-replica rollout validation
- backup/restore validation
- Kubernetes deployment

