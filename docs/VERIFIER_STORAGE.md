# Verifier Storage

## Purpose

The verifier server must become stateless with respect to in-memory session ownership.

Any future API instance should be able to:

- create a session
- append a checkpoint
- return the full receipt chain
- return the current receipt head

without relying on local process memory.

## Current State

Today, `Ows.Verifier.Server` depends on a storage abstraction:

- `IVerifierStorage`

Current implementation:

- `JsonFileVerifierStorage`
- `PostgresVerifierStorage`

Current provider selection:

- `VerifierStorage:Provider=json`
- `VerifierStorage:Provider=postgres`

This JSON-backed store is for local development only.

It is useful because:

- it proves the server can run without in-memory handler state
- it survives process restarts
- it keeps the HTTP surface stable while storage changes underneath

It is not a real institutional trust boundary because:

- the default development mode is still single-node local JSON storage
- the PostgreSQL path still needs deployment-level validation and operational hardening
- idempotency-key columns exist in the PostgreSQL shape but request-key enforcement is not yet wired through the API contract

## Current Code Status

- the PostgreSQL storage adapter now exists
- the server can select it through configuration
- schema migrations are owned by the app, not left as ad-hoc DBA steps
- PostgreSQL startup auto-applies missing verifier migrations
- the verifier server can also run an explicit `migrate` bootstrap path
- append uses a database transaction and session-row locking

## Intended Durable Backend

The production-intended verifier store is PostgreSQL.

PostgreSQL should become the durable source of truth for:

- verifier sessions
- verifier checkpoints
- receipt sequence order
- receipt head state
- optional verifier audit events

Redis must not be the source of truth.

Redis may later help with:

- leases
- heartbeats
- short-lived idempotency cache
- distributed locks
- live state coordination

## Current Storage Boundary

The current abstraction is intentionally narrow:

- `CreateSessionAsync(...)`
- `GetSessionAsync(...)`
- `AppendCheckpointAsync(...)`
- `GetReceiptsAsync(...)`
- `GetHeadAsync(...)`

Required semantics:

- session creation is durable
- checkpoint append is atomic
- checkpoint order is monotonic per session
- session head is derived from durable state
- committed receipt state is never silently overwritten
- same sequence and same payload can return the committed receipt
- same sequence and different payload is rejected

## PostgreSQL Shape

The first durable PostgreSQL design should start with:

- `verifier_sessions`
- `verifier_checkpoints`
- optional `verifier_audit_events`

See [verifier-postgres-foundation.sql](/C:/Users/Liparakis/Desktop/Open%20Work%20Standard/docs/sql/verifier-postgres-foundation.sql) for the current schema draft.

Applied migrations are tracked in `ows_verifier_schema_version`.

## Migration Ownership

OWS should own verifier schema lifecycle.

That means:

- self-hosted operators should be able to run verifier migrations from the app itself
- normal PostgreSQL-backed server startup should also apply any missing verifier migrations
- DevOps still owns provisioning PostgreSQL, secrets, backups, and rollout policy
- DevOps should not have to reverse-engineer the schema from source code or hand-maintain drift-prone SQL steps

Current bootstrap command:

```bash
dotnet run --project src/Ows.Verifier.Server -- migrate
```

This command only works when `VerifierStorage:Provider=postgres` and a PostgreSQL connection string is configured.

## Important Constraint

OWS should not claim that the verifier is a real trust boundary until receipt issuance is durable.

That means:

- JSON persistence is good enough for local development
- PostgreSQL or equivalent durable storage is required before stronger deployment claims
- horizontal scaling is not credible until the durable backend exists
