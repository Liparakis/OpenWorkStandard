# Local Durable Verifier

## Goal

Exercise the Work Verifier against real PostgreSQL in local development.

This is the smallest useful deployment-validation path for the current MVP.

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
.\scripts\start-local-verifier.ps1
.\scripts\status-local-verifier.ps1
.\scripts\logs-local-verifier.ps1
.\scripts\test-local-verifier.ps1
.\scripts\stop-local-verifier.ps1
```

If the verifier DLL has not been built yet, run:

```powershell
dotnet build OWS.sln -nologo
```

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
- rerun the startup helper after fixing the database issue

### Stale PID files

If status reports `stale_pid` or `crashed`:

- run `.\scripts\stop-local-verifier.ps1`
- rerun `.\scripts\start-local-verifier.ps1`

### Paths with spaces

The helpers quote repo and artifact paths. Run them as normal from repositories whose paths contain spaces.

### Reading verifier logs

Use:

```powershell
.\scripts\logs-local-verifier.ps1
.\scripts\logs-local-verifier.ps1 -All
```

The log paths are also shown by `status-local-verifier.ps1`.

## Scope

This is only for local durable-store validation.

It is not yet:

- production hardening
- multi-replica rollout validation
- backup/restore validation
- Kubernetes deployment
