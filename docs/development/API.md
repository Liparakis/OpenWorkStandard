# OWS Verifier API Specification

This document details the HTTP endpoints exposed by the self-hosted OWS verifier.

---

## 1. Authentication and Headers

All API endpoints (except public probes) require the `X-OWS-Verifier-Key` request header.

- **Header name**: `X-OWS-Verifier-Key` (configurable via `VerifierSecurity__HeaderName`).
- **Idempotency**: All write/mutate operations support the `Idempotency-Key` header to prevent duplicate submissions.
- **Correlation**: Every response returns an `X-Request-Id` header for log correlation.

---

## 2. Public Endpoints

These endpoints bypass the API key guard middleware so standard monitoring tools can access them.

### 2.1 GET `/health`
Returns server heartbeat.
- **Response**: `200 OK`
  ```json
  {"status": "Healthy"}
  ```

### 2.2 GET `/ready`
Returns the status of all local and external dependencies.
- **Response**: `200 OK` (or `503 Service Unavailable` when degraded)
  ```json
  {
    "status": "Ready",
    "storage": "postgres",
    "signing": "Enabled",
    "dependencies": {
      "storageProvider": "postgres",
      "storageReady": true,
      "packageStorageReady": true,
      "signingConfigured": true,
      "authMode": "persistent"
    }
  }
  ```

### 2.3 GET `/metrics`
Exposes Prometheus-compatible metrics.
- **Response**: `200 OK`
- **Content-Type**: `text/plain; version=0.0.4; charset=utf-8`

---

## 3. Session Endpoints

### 3.1 POST `/sessions`
Starts a new provenance session.
- **Allowed Roles**: `Operator`, `InstitutionAdmin`, `StudentClient`
- **Scoping**: Non-operators must match the `institutionId` parameter. Bound `StudentClient` keys must also match the `studentUserId` parameter.
- **Request Body**: Optional identifiers are opaque external context metadata. OWS stores and scopes them but does not resolve institutions, courses, rosters, or assessments.
  ```json
  {
    "institutionId": "inst-1",
    "assessmentId": "assess-1",
    "studentUserId": "student-123",
    "courseOfferingId": "offering-1"
  }
  ```
- **Response**:
  ```json
  {"sessionId": "f1d2e3b4a5c6d7..."}
  ```

### 3.2 POST `/sessions/{id}/heartbeat`
Records a session heartbeat to extend the lease.
- **Allowed Roles**: `Operator`, `StudentClient` (scoped)
- **Request Body**:
  ```json
  {"lastKnownEventHash": "sha256-hash-here"}
  ```

### 3.3 POST `/sessions/{id}/checkpoints`
Appends a timeline checkpoint block.
- **Allowed Roles**: `Operator`, `StudentClient` (scoped)
- **Headers**: `Idempotency-Key` (required)
- **Request Body**:
  ```json
  {
    "sessionId": "session-id",
    "sequenceNumber": 5,
    "timelineHeadHash": "sha256-hash-here"
  }
  ```

---

## 4. Package Endpoints

### 4.1 POST `/packages`
Registers package metadata before uploading.
- **Allowed Roles**: `Operator`, `StudentClient` (scoped)
- **Request Body**:
  ```json
  {
    "sessionId": "session-id",
    "packageSha256": "64-char-hex-hash",
    "packageSizeBytes": 48209,
    "objectStorageProvider": "local",
    "objectBucket": "packages",
    "objectKey": "hash-filename.owspkg"
  }
  ```

### 4.2 POST `/packages/upload`
Uploads a package `.owspkg` file directly.
- **Allowed Roles**: `Operator`, `StudentClient` (scoped)
- **Content-Type**: `multipart/form-data`
- **Form Fields**:
  - `file`: The `.owspkg` file bytes.
  - `sessionId`: Associated session ID.

### 4.3 GET `/packages/{id}`
Reads package metadata.
- **Allowed Roles**: `Operator`, `InstitutionAdmin` (scoped), `InstructorReviewer` (scoped), `StudentClient` (bound + owner-scoped only; unbound cannot read).

### 4.4 GET `/packages/{id}/verification`
Returns the raw JSON verification result.
- **Allowed Roles**: `Operator`, `InstitutionAdmin` (scoped), `InstructorReviewer` (scoped), `StudentClient` (bound + owner-scoped only; unbound cannot read).
