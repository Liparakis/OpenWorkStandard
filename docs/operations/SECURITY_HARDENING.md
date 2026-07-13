# Security Hardening

## Signing Key Custody

The verifier receipt signing key (`VerifierStorage__ReceiptSigningKey`) is the most sensitive operational secret.

### Where signing keys are configured

The signing key is configured via the `VerifierStorage__ReceiptSigningKey` environment variable, set in the Compose `.env` file or injected via a secret management tool.

### Default and development keys are unsafe

In `Production` environment mode, the verifier refuses to start if the signing key is absent or too short (fewer than 16 characters). In development mode it logs a warning but continues. **Never run a pilot or student-facing deployment with a dev-mode signing key.**

### How to find the current signing key fingerprint

The verifier logs the signing key fingerprint (a safe, non-secret SHA-256 derived identifier) at startup:

```bash
docker compose logs ows-verifier | grep "Signing Key Fingerprint"
# Signing Key Fingerprint: sha256:<hex>
```

The fingerprint is also returned by `GET /diagnostics/summary` as `signingKeyFingerprintPresent: true/false`. The actual fingerprint is **not** returned by the API — only its presence is confirmed.

### Who should have access

- Signing key material should be held by no more than two operators.
- Treat the signing key like a root credential.
- Store it in a password manager (e.g. Bitwarden, 1Password) or a secret management tool (e.g. HashiCorp Vault).
- Do not store it in the same location as the database dump.
- Do not share it over unencrypted channels.

### How to back up the signing key

1. Record the raw key in your password manager under a named entry (e.g. `OWS Verifier Signing Key - Pilot 2025`).
2. Record the fingerprint alongside it.
3. Record the date the key was activated.
4. Store a second copy in an offline or write-protected location.

### What happens if the signing key is lost

If the signing key is permanently lost:

- Receipts issued under that key can no longer be verified using the chain verifier.
- Sessions and packages verified under that key will show a fingerprint mismatch.
- You must document the date range the key was active and treat those sessions as having a non-recoverable signing gap.
- Create a new signing key for future sessions.

### What happens if the signing key is compromised

1. Immediately rotate to a new key by updating `VerifierStorage__ReceiptSigningKey` and restarting the verifier.
2. Document the old key fingerprint and the date it was retired.
3. Receipts already issued under the old key retain their validity (the chain is still mathematically valid), but you must treat the trust boundary as degraded if the old key may have been used to forge receipts.
4. Audit recent sessions for unusual patterns.

### Key rotation

Key rotation is deferred in v0.1. The verifier uses a single active signing key. When rotating:

1. Issue the new key.
2. Record the old key fingerprint and retire date.
3. Update `VerifierStorage__ReceiptSigningKey`.
4. Restart the verifier.
5. Confirm the new fingerprint appears in startup logs.
6. Old receipts validated against the old fingerprint remain valid for their historical sessions — they are read-only and immutable.

---

## Auth/RBAC v0.2

Current pilot-grade verifier auth uses API keys first, with an optional OIDC/JWT bearer foundation for future human-facing access.

Implemented:

- `Operator`: full verifier access, including API key lifecycle management.
- `InstitutionAdmin`: read/write access scoped to one institution (metadata and sessions), and ability to create `InstructorReviewer` and `StudentClient` keys for the same institution.
- `InstructorReviewer`: read-only access scoped to one institution.
- `StudentClient`: student-facing access (either bound to a student user ID or unbound). Allowed to start sessions, send heartbeats, append checkpoints, and upload packages. Bound keys can read their own package status and reports, while unbound keys are restricted from reading packages/reports to prevent cross-student exposure.

Deferred:

- browser login/session flows
- SAML

OIDC/JWT bearer foundation notes:

- disabled by default
- configured through `VerifierAuth__Oidc__*`
- validated bearer claims are mapped into the same internal verifier access context used by API keys
- existing endpoint RBAC is reused; there is no second role system
- requests that send both API key and bearer credentials are rejected with `400` and audited as `auth.ambiguous`

Persisted verifier API keys are stored as:

- key prefix
- key hash
- role
- optional institution scope
- created timestamp
- optional expiry
- optional last-used timestamp
- optional revocation timestamp

Raw API keys are returned once on creation and are not stored afterward.

## Observability v0.1

The verifier now records safe audit events for:

- `api_key.created`
- `api_key.revoked`
- `auth.failed`
- `auth.ambiguous`
- `access.denied`
- `session.created`
- `checkpoint.accepted`
- `heartbeat.accepted`
- `lease.gap.detected`
- `package.submitted`
- `package.verified`
- `report.read`
- `readiness.failed`

Request correlation:

- the verifier accepts `X-Request-Id`
- if absent, the verifier generates one
- the response echoes `X-Request-Id`
- request ids appear in request logs and audit events

The verifier intentionally does not log:

- raw API keys
- signing keys
- connection strings
- package contents
- full student payloads

## HTTP Abuse Controls

Current built-in controls:

- endpoint-scoped fixed-window rate limits are enabled by default through `VerifierRateLimiting__*`
- auth management endpoints use a stricter limiter than read endpoints
- package upload endpoints use a separate low-volume limiter
- `/ready` and `/metrics` stay public for monitoring, but they are still rate-limited
- multipart request parsing is capped from `VerifierStorage__MaxPackageSizeBytes`

Default minute buckets:

- public probes: `60`
- auth management: `10`
- package uploads: `6`
- session writes: `30`
- authenticated reads: `120`
- diagnostics/audit reads: `30`

Current package-admission checks:

- `.owspkg` extension required
- max compressed upload size from `VerifierStorage__MaxPackageSizeBytes`
- max expanded archive size from `VerifierStorage__MaxPackageExpandedBytes`
- max archive entry count from `VerifierStorage__MaxPackageEntryCount`
- duplicate archive entry rejection
- unsafe archive path rejection
- unsafe compression-ratio rejection
- required `manifest.json`, `timeline.jsonl`, and `version_graph.json` entries required

Known limits:

- JSON endpoint body limits are still the default ASP.NET Core limits; only package multipart uploads have an explicit server-side cap today
- public probes remain intentionally unauthenticated and should sit behind a reverse proxy if broad internet exposure is expected
- audit coverage in v0.1 is focused on verifier writes, package verification, and scoped reads rather than management-system operations

Audit query limits:

- `GET /audit/events` defaults to `100`
- caller-supplied limits above `500` are clamped to `500`
- zero, negative, or missing limits fall back to `100`

Operator diagnostics:

- `GET /audit/events`
- `GET /diagnostics/summary`

The diagnostics summary now includes:

| Field | Description |
|---|---|
| `environment` | Current environment mode (Development/Production) |
| `storageProvider` | Active storage provider (json/postgres) |
| `packageStorageConfigured` | Whether `LocalStoragePath` is set |
| `packageStorageReady` | Whether the blob directory is accessible |
| `packageBlobCount` | Count of `.owspkg` blobs in storage (null if not accessible) |
| `signingKeyFingerprintPresent` | Whether a non-empty signing key fingerprint is computed |
| `authMode` | Active auth mode (none/bootstrap/persisted) |
| `oidc` | Safe OIDC/JWT bearer status (`enabled`, `authorityConfigured`, `audienceConfigured`, `roleClaimConfigured`) |
| `metrics` | Audit event aggregate counts |
| `packageVerificationJobs` | Job counts by status (pending/running/succeeded/failed) |

These endpoints are operator-only when API-key auth is enabled. They are designed for pilot diagnostics, not for a full monitoring pipeline.

## Operator Guide

### Create a bootstrap operator key

Set:

- `VerifierSecurity__ApiKey=<strong-bootstrap-key>`

Use that key in `X-OWS-Verifier-Key` to call the API key lifecycle endpoints.

### Create a persisted operator key

`POST /auth/api-keys`

```json
{
  "role": "Operator"
}
```

### Create a reviewer key

`POST /auth/api-keys`

```json
{
  "role": "InstructorReviewer",
  "institutionId": "institution-1",
  "expiresAtUtc": "2026-12-31T23:59:59Z"
}
```

### List keys

`GET /auth/api-keys`

Returns metadata only. Raw secrets are not returned.

### Revoke a key

`POST /auth/api-keys/{id}/revoke`

### Rotate a key

1. Create a replacement key.
2. Distribute the new key.
3. Confirm the new key works.
4. Revoke the old key.

### Configure shared bootstrap guard

Use `VerifierSecurity__ApiKey` only for bootstrap compatibility or tightly controlled operator access.

For self-hosted pilots, prefer persisted keys after bootstrap.

### Read logs and correlate requests

Every verifier response includes `X-Request-Id`.

Use that value to:

1. find the matching request log line
2. query `GET /audit/events`
3. correlate operator investigation notes

### Query audit events

`GET /audit/events?eventType=package.verified&packageId=<submissionId>`

Supported filters:

- `institutionId`
- `sessionId`
- `packageId`
- `eventType`
- `since`
- `limit`

### Read diagnostics summary

`GET /diagnostics/summary`

This returns safe aggregate counts and current mode/provider details. It is not a substitute for Prometheus/Grafana/Loki.

## Recommended Pilot Setup

1. Configure PostgreSQL, receipt signing, and one bootstrap operator key.
2. Start the verifier and run migrations.
3. Create persisted operator keys for administrators.
4. Create institution-scoped `InstructorReviewer` keys for reviewers.
5. Stop distributing the bootstrap key for daily use.
6. Set expiries on reviewer keys when operationally practical.
