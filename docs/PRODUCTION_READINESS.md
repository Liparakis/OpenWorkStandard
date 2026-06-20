# OWS Verifier Production Readiness

This document outlines the production-readiness status, operational security postures, and deployment baselines for self-hosting the Open Work Standard (OWS) verifier during pilot programs.

---

## 1. Readiness Matrix

| Feature / Dimension | Status | Target Phase | Notes / Requirements |
|---|---|---|---|
| **Auth/RBAC: operator keys** | **Pilot-ready** | Pilot | Persistent or config bootstrap options configured via environment. |
| **Auth/RBAC: student client keys** | **Pilot-ready** | Pilot | Key delegation scoping restricts student keys to own sessions and package uploads. |
| **Data Isolation & Scoping** | **Pilot-ready** | Pilot | Multi-institution scoping prevents cross-institution metadata and event sniffing. |
| **Prometheus Metrics** | **Pilot-ready** | Pilot | Lightweight dynamic text-exposition `/metrics` endpoint available anonymously. |
| **Signing Key Custody** | **Pilot-ready** | Pilot | Receipts signed via `VerifierStorage__ReceiptSigningKey`. Keys must not be checked into Git. |
| **Backup and Restore Drills** | **Pilot-ready** | Pilot | Documented runbooks cover PostgreSQL dumps, package blob volumes, and restore validation. |
| **HTTPS / TLS** | **Needs hardening** | Production | Must be terminated at a reverse proxy (e.g. Nginx, Caddy) or cloud load balancer. |
| **Secrets Management** | **Needs hardening** | Production | Environment variables should be managed via Docker Secrets, Vault, or AWS KMS. |
| **SSO / OIDC Authentication** | **Deferred** | Scale | Institutional user dashboards will require SAML/OIDC. API endpoints use API keys. |
| **Kubernetes Clustering** | **Deferred** | Scale | Docker Compose is the default deployment template; Helm charts are deferred. |
| **Distributed Cache & Queue** | **Deferred** | Scale | PostgreSQL handles verification job states and metadata queue; Redis/NATS are deferred. |

---

## 2. Security and Hardening Checklists

### 2.1 Encryption-in-Transit (TLS)
- The OWS verifier container does **not** terminate TLS itself.
- In production, you **must** configure a reverse proxy (e.g., Nginx, Caddy, HAProxy) in front of the verifier to terminate TLS and forward requests over HTTP.
- Enforce TLS 1.3 or TLS 1.2 strong cipher suites.
- Set `HSTS` headers to ensure clients do not fallback to unencrypted HTTP connections.

### 2.2 Key Material Custody
- **Receipt Signing Key**: The value of `VerifierStorage__ReceiptSigningKey` is a cryptographic secret.
  - Treat it like a root Certificate Authority (CA) key.
  - Do not use development defaults (`change-me`, `development`, etc.) in production. The server enforces a minimum 16-character length for production environments.
  - Record the key fingerprint (`docker compose logs ows-verifier | grep Fingerprint`) in your offline vault.
- **Operator API Keys**:
  - The bootstrap `VerifierSecurity__ApiKey` must be rotated immediately if compromised.
  - Persistent keys created via `POST /auth/api-keys` return the raw key only once. They must be stored in password managers immediately.

### 2.3 Volume Storage and DB Backups
- Database backups should run daily using `pg_dump`.
- Named Docker volume `ows-verifier-package-data` contains all uploaded `.owspkg` blobs. These must be archived using standard file/tar backups alongside PostgreSQL backups to prevent metadata/blob inconsistencies.
- Store backups on separate physical media or cloud storage buckets with access controls.

---

## 3. Scale and Performance Constraints
- **Database Backend**: PostgreSQL is required for multi-institution pilots. JSON file storage is restricted to development environments.
- **Worker Concurrency**: The verifier features a worker-loop (`PackageVerificationWorker`) that processes jobs sequentially per instance. For pilot volumes, this is sufficient.
- **Horizontal Scaling**: Since job storage resides in PostgreSQL, multiple verifier instances can run concurrently behind a load balancer. Job leases prevent concurrent double-processing of the same verification job.
