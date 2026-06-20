Status: Active  
Audience: Operator  
Last reviewed: 2026-06-20

# OWS Self-Hosting Guide

## Scope

This guide is for operators who want to run the current Open Work Standard verifier themselves.

It describes the current MVP verifier path only:

- ASP.NET Core verifier server
- PostgreSQL-backed durable storage
- app-owned migrations

It does not describe:

- multi-tenant rollout
- package submission API
- Kubernetes manifests
- Redis or NATS

## What You Are Hosting

Today, the useful self-hosted component is `src/Ows.Verifier.Server`.

The repository now includes a verifier server Dockerfile at:

- `src/Ows.Verifier.Server/Dockerfile`

Current HTTP surface:

- `POST /auth/api-keys`
- `GET /auth/api-keys`
- `POST /auth/api-keys/{id}/revoke`
- `POST /sessions`
- `POST /sessions/{id}/checkpoints`
- `POST /packages`
- `GET /packages/{id}`
- `GET /sessions/{id}/packages`
- `GET /sessions/{id}/receipts`
- `GET /sessions/{id}/head`

## Minimum Production-Intended Baseline

Use this baseline if you want stronger trust claims than local development:

- PostgreSQL as verifier storage
- S3-compatible object storage for `.owspkg` blobs before package registration
- TLS at the edge
- secrets outside the repository
- durable backup policy
- controlled operator access
- retained logs appropriate to institutional policy

Do not use JSON storage for anything beyond local development.

Do not store `.owspkg` package blobs in PostgreSQL. `POST /packages` registers object storage metadata only.

## Configuration

Required verifier settings:

- `VerifierStorage__Provider=postgres`
- `VerifierStorage__PostgresConnectionString=<your-postgres-connection-string>`
- `VerifierStorage__ReceiptSigningKey=<secret-outside-the-repo>`
- `VerifierSecurity__ApiKey=<operator-api-key>` or `VerifierSecurity__ApiKeys__<n>__*`

Example:

```powershell
$env:VerifierStorage__Provider = "postgres"
$env:VerifierStorage__PostgresConnectionString = "Host=db.example;Port=5432;Database=ows_verifier;Username=ows;Password=change-me"
$env:VerifierStorage__ReceiptSigningKey = "<long-random-secret>"
$env:VerifierSecurity__ApiKey = "<bootstrap-operator-key>"
$env:OWS_VERIFIER_API_KEY = "<bootstrap-operator-key>"
```

## First Bootstrap

1. Provision PostgreSQL.
2. Set the verifier environment variables.
3. Run the migration bootstrap:

```bash
dotnet run --project src/Ows.Verifier.Server -- migrate
```

4. Start the verifier server:

```bash
dotnet run --project src/Ows.Verifier.Server
```

Or build the verifier image:

```bash
docker build -f src/Ows.Verifier.Server/Dockerfile -t ows-verifier:local .
```

Then run it with PostgreSQL configuration injected at runtime.

Verified local image build:

- image tag: `ows-verifier:local`
- Dockerfile: `src/Ows.Verifier.Server/Dockerfile`
- last validated: 2026-06-19

## Ongoing Startup

The verifier also applies missing PostgreSQL migrations during normal startup.

That is acceptable for MVP and simple self-hosting, but not the final answer for stricter multi-replica production rollout.

See `docs/reference/DEFERRED_NOTES.md` for that explicit deferral.

Receipts are HMAC-signed when `VerifierStorage__ReceiptSigningKey` is configured. Keep that key outside the repository and deployment images. Public-key signatures, key IDs, and rotation are still deferred.

Requests require `X-OWS-Verifier-Key` when verifier API keys are configured. The current v0.1 model supports full-access `Operator` keys and read-only institution-scoped `InstructorReviewer` keys. It is still API-key auth, not user identity or full institutional RBAC.

Preferred pilot setup:

1. Start with a bootstrap operator key through `VerifierSecurity__ApiKey`.
2. Create persisted operator and reviewer keys through `POST /auth/api-keys`.
3. Distribute only persisted keys to users.
4. Rotate the bootstrap key out of daily use once persisted operator keys exist.

## What To Check After Startup

- the verifier can create a session
- the verifier can append a checkpoint
- receipts persist across process restart
- session head is still available after restart
- logs are readable
- backups exist for the PostgreSQL database

## Local Validation Path

Before institutional hosting, validate the flow locally:

- follow `docs/development/VERIFIER_LOCAL_DEV.md`
- use `docker-compose.local.yml`
- run the helper scripts
- confirm the smoke test passes

## What Not To Claim Yet

Do not claim that the current self-hosted verifier already provides:

- always-on local capture guarantees
- institution/user/course management
- full operational hardening
- production-grade multi-node rollout discipline

The current Docker support is verifier-image-level only. Full production Compose or Helm packaging is still deferred.

## Recommended Next Operational Steps

1. Keep the verifier API narrow.
2. Validate PostgreSQL backups and restore.
3. Use persisted API keys for pilot operators and reviewers.
4. Add full identity and broader RBAC before broader institutional exposure.

