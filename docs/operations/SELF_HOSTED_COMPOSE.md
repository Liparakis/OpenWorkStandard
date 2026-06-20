# Self-Hosting with Docker Compose

This guide explains how to deploy the Open Work Standard (OWS) verifier server in a self-hosted environment using Docker Compose. This deployment model is designed for pilot programs and local institutional testing.

---

## Architecture Overview

The default Compose stack packages two core components:
1. **`postgres`**: A PostgreSQL 17 database service using a persistent named volume (`ows-postgres-data-prod`) for durable notarization storage.
2. **`ows-verifier`**: The ASP.NET Core OWS Verifier Web API service, running on port `8080` internally and mounting a named volume (`ows-verifier-package-data`) for durable `.owspkg` blob storage.

For the supported multi-instance pilot pattern, also see `deploy/compose/docker-compose.multi-instance.yml` and `docs/operations/MULTI_INSTANCE_DEPLOYMENT.md`.

---

## 1. Prerequisites

Before starting, ensure the host system has:
- **Docker** (v20.10 or newer)
- **Docker Compose** (v2.0 or newer)
- Port **5078** free on the host network (or customizable in `.env`)
- Port **5432** free on the host network (if PostgreSQL is exposed externally)

---

## 2. Configuration (`.env`)

1. Navigate to the compose directory:
   ```bash
   cd deploy/compose
   ```
2. Copy the environment template file:
   ```bash
   cp .env.example .env
   ```
3. Edit the `.env` file to customize your configurations. Make sure to set strong, private secrets:

> [!IMPORTANT]
> **Production Key Enforcement**: When `VerifierEnvironment` is set to `Production`, the verifier will execute startup preflight checks and abort immediately if weak configurations or default keys are detected.
> Ensure that `VerifierSecurity__ApiKey` and `VerifierStorage__ReceiptSigningKey` are at least 16 characters long when used as bootstrap secrets.

---

## 3. Starting the Stack

Build the OWS verifier Docker image locally if you haven't already:
```bash
docker build -t ows-verifier:latest -f src/Ows.Verifier.Server/Dockerfile .
```

Start the containers in detached (background) mode:
```bash
docker compose up -d
```

### Run Migrations
On the initial start, execute the database migration tool inside the verifier container:
```bash
docker compose exec ows-verifier dotnet Ows.Verifier.Server.dll migrate
```

For multi-instance deployments, run `migrate` once before starting all steady-state instances, then set `VerifierStorage__ApplyMigrationsOnStartup=false`.

---

## 4. Health and Observability Checks

Verify the system is running and database connections are healthy:

### 1. `/health` Endpoint
Confirms the ASP.NET Core web server is online:
```bash
curl -f http://localhost:5078/health
# Returns: { "status": "Healthy" }
```

### 2. `/ready` Endpoint
Checks safe dependency state for storage, education store reachability, package storage, signing configuration, auth mode, OIDC/JWT bearer status, worker mode, and migration mode:
```bash
curl -f http://localhost:5078/ready
# Returns: { "status": "Ready", "storage": "postgres", ... } (or HTTP 503 if dependencies are unhealthy)
```

### 3. `/diagnostics/summary` Endpoint
Returns lightweight safe counters for pilot operations:
```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/diagnostics/summary
```

The response includes package-storage readiness, worker mode (`api+worker` vs `api-only`), migration mode, OIDC/JWT bearer status, warnings, and verification-job counters so operators can tell whether uploads are only accepted, actively processing, or piling up.

### 4. `/audit/events` Endpoint
Returns operator-only audit events with simple filters:
```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" \
  "http://localhost:5078/audit/events?eventType=package.verified&limit=20"
```

### 5. Read Container Logs
To inspect verifier logs in real time:
```bash
docker compose logs -f ows-verifier
```

Each response also includes `X-Request-Id`. Use that id to correlate container logs with `/audit/events`.

### 6. Optional External Observability

The base self-hosted stack does not require Prometheus, Grafana, Loki, or Promtail.

Normal self-hosting:

```bash
docker compose -f deploy/compose/docker-compose.yml up
```

Optional observability overlay:

```bash
docker compose -f deploy/compose/docker-compose.yml -f deploy/compose/docker-compose.observability.yml up
```

See [OBSERVABILITY.md](OBSERVABILITY.md) for the optional stack.

---

## 5. Security Warnings and Best Practices

> [!CAUTION]
> **Production Deployment Hardening Checklist**:
> 1. **TLS / HTTPS Termination**: The OWS Verifier does NOT handle TLS termination natively in the container. **You must run a reverse proxy (e.g. Nginx, Traefik, Caddy, or an AWS ALB) in front of the Compose stack to enforce HTTPS.**
> 2. **Secret Privacy**: Never commit the `.env` file to version control.
> 3. **API Key Guard limits**: The bootstrap key (`VerifierSecurity__ApiKey`) is compatibility/bootstrap-only. Persisted operator/reviewer keys are preferred for pilots, and they still do not replace full OAuth/OIDC authentication or fine-grained institutional RBAC.
> 4. **OIDC/JWT bearer is optional**: `VerifierAuth__Oidc__Enabled=false` remains the default. API keys stay the primary pilot path for CLI, VS Code, watchers, and automation.
> 5. **No dual-auth requests**: Requests that send both `X-OWS-Verifier-Key` and `Authorization: Bearer` are rejected with `400 Bad Request` and a safe audit event.
> 4. **No Dev Keys**: Double-check that dev-mode default signing keys are not active in production environments.
> 5. **Observability scope**: `GET /diagnostics/summary` and `GET /audit/events` remain built-in pilot-grade operator tools. Prometheus/Grafana/Loki/Promtail are optional external helpers, not required infrastructure.
> 6. **Grafana/Loki protection**: If you enable the optional overlay, protect Grafana and Loki with your own auth, reverse proxy, and network controls.

Optional OIDC/JWT bearer example:

```text
VerifierAuth__Oidc__Enabled=true
VerifierAuth__Oidc__Authority=https://idp.example
VerifierAuth__Oidc__Audience=ows-verifier
VerifierAuth__Oidc__RoleClaim=role
VerifierAuth__Oidc__InstitutionClaim=institution
VerifierAuth__Oidc__UserIdClaim=sub
```

This is a backend auth foundation only. Browser login flows, dashboard UI, and SAML are still deferred.

---

## 6. Backups and Upgrades

### Database Backup
The PostgreSQL database stores all notarized session heads and checkpoints. Back up the PostgreSQL named volume by copying the raw files or using `pg_dump`:
```bash
docker compose exec -t postgres pg_dump -U ows -d ows_verifier > ows_backup_$(date +%F).sql
```

### Package Storage Backup
Submitted student `.owspkg` package archives are stored in the named volume configured under `VerifierStorage__LocalStoragePath`. Back up that volume together with PostgreSQL or you will retain package metadata without the package bytes needed for re-verification or report regeneration.

### Package Verification Lifecycle

- `POST /packages/upload` stores the `.owspkg` blob durably and queues verification.
- `POST /packages` still supports metadata-first registration when you need to anchor package metadata before bytes are uploaded.
- the in-process worker reads pending jobs from durable storage and writes `Pending`, `Running`, `Completed`, or `Failed` status back to the database
- stale `Running` jobs are reset to `Pending` after the configured timeout when the verifier restarts

For MVP self-hosting, this is enough. External object storage, Redis queues, and separate workers are still deferred.

Worker configuration for pilots:

- `PackageVerificationWorker__Enabled=true` for the single-node baseline
- `PackageVerificationWorker__Enabled=true` on exactly one worker instance in multi-instance mode
- `PackageVerificationWorker__Enabled=false` on API-only instances

Multi-instance mode also requires one shared package storage path mounted into every instance.

### Upgrades
To upgrade to a new release:
1. Pull or build the latest `ows-verifier` image.
2. Stop the current stack: `docker compose down`.
3. Restart the stack: `docker compose up -d`.
4. Rerun migrations: `docker compose exec ows-verifier dotnet Ows.Verifier.Server.dll migrate`.

### Multi-instance pattern

The supported pilot pattern today is deliberately boring:

- multiple API instances against one PostgreSQL database
- one worker-enabled instance
- one shared package blob volume/path
- no Kubernetes
- no Redis
- no NATS

Use `deploy/compose/docker-compose.multi-instance.yml` as the reference pattern.

---

## 7. Operator Resources

| Document | Purpose |
|---|---|
| [OPERATIONS_RUNBOOK.md](OPERATIONS_RUNBOOK.md) | Day-to-day operator procedures, startup checklist, emergency procedures |
| [BACKUP_RESTORE.md](BACKUP_RESTORE.md) | What to back up, restore order, recovery failure modes, recovery drill |
| [SECURITY_HARDENING.md](SECURITY_HARDENING.md) | Signing key custody, API key management, diagnostics fields reference |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Common symptoms, causes, and fixes |
| [OBSERVABILITY.md](OBSERVABILITY.md) | Optional Prometheus, Grafana, Loki, and Promtail overlay |

Run the ops readiness check at any time:

```powershell
.\scripts\windows\verify-ops-readiness.ps1 -BaseUrl http://localhost:5078 -ApiKey "<operator-key>"
```

