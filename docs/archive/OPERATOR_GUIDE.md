> Archived: this document may be outdated. See `../operations/SELF_HOSTING.md` and `../workflows/PILOT_DEMO.md` for current operator guidance.

# OWS Verifier Operator Guide

This guide is designed for systems administrators and operators running a self-hosted OWS verifier instance.

For the end-to-end pilot walkthrough, start with [PILOT_DEMO.md](../workflows/PILOT_DEMO.md). It covers fixture creation, student submission, reviewer report access, audit checks, diagnostics, and negative-path validation.

---

## 1. Role Scopes

The verifier uses Role-Based Access Control (RBAC) to enforce security scopes. All requests must carry an authorized API key in the `X-OWS-Verifier-Key` header.

| Role | Scope | Permitted Actions |
|---|---|---|
| **Operator** | Global | Full access. Can create/revoke any API keys, read diagnostics, view global audit feeds, and manage all metadata. |
| **InstitutionAdmin** | Institution | Manage education metadata (users, course offerings, assessments) and create delegated keys (InstructorReviewer, StudentClient) within their own institution scope. |
| **InstructorReviewer** | Institution | Read-only access to packages, verification reports, and session metadata within their institution scope. |
| **StudentClient** | Institution + Student | Bound or unbound client access. Can start sessions, send heartbeats, append checkpoints, and upload packages. Bound keys can read their own package status and reports. |

---

## 2. API Key Management Commands

All API key operations are performed via REST endpoints. The bootstrap Operator key configured via `VerifierSecurity__ApiKey` is used to create the initial admin keys.

### 2.1 Create an InstitutionAdmin Key
```bash
curl -X POST -H "X-OWS-Verifier-Key: <bootstrap-key>" \
  -H "Content-Type: application/json" \
  -d '{"role": "InstitutionAdmin", "institutionId": "university-of-oxford"}' \
  http://localhost:5078/auth/api-keys
```
*Response*:
```json
{
  "apiKey": "ows_a1b2c3d4e5f6g7h8i9j0...",
  "metadata": {
    "keyId": "9b1deb4d3b7d4babab85223b9d613123",
    "keyPrefix": "ows_a1b2c3d4",
    "role": "InstitutionAdmin",
    "institutionId": "university-of-oxford",
    "createdAtUtc": "2026-06-20T13:40:00Z"
  }
}
```

### 2.2 Create a Bound StudentClient Key
Delegated by an `InstitutionAdmin` of that institution:
```bash
curl -X POST -H "X-OWS-Verifier-Key: <institution-admin-key>" \
  -H "Content-Type: application/json" \
  -d '{"role": "StudentClient", "institutionId": "university-of-oxford", "studentUserId": "student-4829"}' \
  http://localhost:5078/auth/api-keys
```

### 2.3 Create an Unbound StudentClient Key
```bash
curl -X POST -H "X-OWS-Verifier-Key: <institution-admin-key>" \
  -H "Content-Type: application/json" \
  -d '{"role": "StudentClient", "institutionId": "university-of-oxford"}' \
  http://localhost:5078/auth/api-keys
```

### 2.4 List All Metadata (Operator Only)
```bash
curl -H "X-OWS-Verifier-Key: <operator-key>" http://localhost:5078/auth/api-keys
```

### 2.5 Revoke an API Key (Operator Only)
```bash
curl -X POST -H "X-OWS-Verifier-Key: <operator-key>" \
  http://localhost:5078/auth/api-keys/9b1deb4d3b7d4babab85223b9d613123/revoke
```

---

## 3. Run The Full Pilot Dry Run

Use the repo-owned rehearsal script before demos or pilot support sessions:

```powershell
$env:VerifierSecurity__ApiKey = "pilot-operator-key-12345"
$env:OWS_VERIFIER_API_KEY = "pilot-operator-key-12345"
$env:VerifierStorage__ReceiptSigningKey = "pilot-signing-key-12345"
.\scripts\windows\run-live-pilot-dry-run.ps1
```

What it does:

- starts the local verifier helper path
- creates a unique institution/course/student/assessment fixture
- runs the student CLI watch, heartbeat, checkpoint, package, and upload path
- waits for package verification to complete
- validates reviewer read-only access and operator diagnostics/audit access
- writes `artifacts/pilot-demo/live-dry-run-summary.json`

---

## 4. Scrape Metrics

The metrics endpoint is designed for integration with Prometheus. It bypasses authentication so monitoring agents can scrape it without API keys.

```bash
curl http://localhost:5078/metrics
```
*Expected text output format:*
```text
# HELP ows_sessions_created_total Total number of OWS assessment sessions created.
# TYPE ows_sessions_created_total counter
ows_sessions_created_total 12
...
```
Configure your `prometheus.yml` to target the verifier server:
```yaml
scrape_configs:
  - job_name: 'ows-verifier'
    scrape_interval: 15s
    static_configs:
      - targets: ['ows-verifier:5078']
```
> [!WARNING]
> Archived document. This may contain outdated design notes.
> Current guidance lives under docs/core/, docs/workflows/, docs/operations/, and docs/integrations/.
