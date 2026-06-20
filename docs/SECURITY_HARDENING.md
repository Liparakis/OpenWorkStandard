# Security Hardening

## Auth/RBAC v0.1

Current pilot-grade verifier auth uses API keys, not user login.

Implemented:

- `Operator`: full verifier access, including API key lifecycle management
- `InstructorReviewer`: read-only access scoped to one institution

Deferred:

- `InstitutionAdmin`
- `StudentClient`
- SSO/OIDC/SAML

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

Operator diagnostics:

- `GET /audit/events`
- `GET /diagnostics/summary`

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
