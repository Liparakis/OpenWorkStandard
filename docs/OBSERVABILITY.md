# OWS Observability

This document describes the optional Prometheus, Grafana, Loki, and Promtail integration for the Open Work Standard verifier.

This stack is opt-in. Base self-hosting does not require it.

## What This Covers

Optional pilot observability includes:

- Prometheus scraping verifier `/metrics`
- Grafana loading one verifier overview dashboard
- Loki storing verifier container logs
- Promtail shipping verifier container logs to Loki

This is pilot observability, not a hardened production monitoring system.

## What Base Self-Hosting Requires

Normal self-hosting:

```bash
docker compose -f deploy/compose/docker-compose.yml up
```

Self-hosting with optional observability:

```bash
docker compose -f deploy/compose/docker-compose.yml -f deploy/compose/docker-compose.observability.yml up
```

The base verifier stack still works without Prometheus, Grafana, Loki, or Promtail.

## Files

Observability assets live under:

- `deploy/compose/docker-compose.observability.yml`
- `deploy/observability/prometheus.yml`
- `deploy/observability/loki.yml`
- `deploy/observability/promtail.yml`
- `deploy/observability/grafana/provisioning/`
- `deploy/observability/grafana/dashboards/ows-verifier-overview.json`

## Metrics Exposed by `/metrics`

The verifier currently exposes:

- `ows_sessions_created_total`
- `ows_checkpoints_accepted_total`
- `ows_heartbeats_accepted_total`
- `ows_package_uploads_total`
- `ows_package_verification_successes_total`
- `ows_package_verification_failures_total`
- `ows_package_verification_jobs_total{status=...}`
- `ows_auth_failures_total`
- `ows_access_denied_total`
- `ows_audit_events_total`
- `ows_ready_dependency_status{dependency=...}`
- `ows_package_verification_worker_enabled`
- `ows_instance_mode{mode=...}`

Not exposed in v0.1:

- request count metrics
- request duration metrics
- alerting rules
- SLOs or production paging policy

The dashboard intentionally does not fake missing request panels.

## Starting the Optional Stack

1. Start the base stack:

```bash
docker compose -f deploy/compose/docker-compose.yml up -d
```

2. Start the overlay:

```bash
docker compose -f deploy/compose/docker-compose.yml -f deploy/compose/docker-compose.observability.yml up -d
```

3. Open:

- Grafana: `http://localhost:3000`
- Prometheus: `http://localhost:9090`
- Loki: `http://localhost:3100`

Default Grafana credentials from the compose overlay:

- username: `admin`
- password: `admin`

Change those in real deployments.

## Grafana Dashboard

Provisioned dashboard:

- `Open Work Standard / OWS Verifier Overview`

Provisioning is automatic. Manual dashboard import is not required.

Panels include:

- sessions created
- checkpoints accepted
- heartbeats accepted
- package uploads
- verification jobs by status
- verification successes/failures
- auth failures and access denied
- audit event count
- readiness dependency status
- worker enabled
- verifier logs from Loki

## Loki and Promtail

Promtail is used in v0.1 because it is smaller and easier to explain than Alloy.

Current log collection behavior:

- Promtail discovers Docker containers through the Docker socket
- it ships logs only for the `ows-verifier-prod` container
- logs are sent to Loki
- Grafana can query those logs through the provisioned Loki datasource

Promtail is optional. If you do not start the overlay compose file, no Loki or Promtail services run.

Grafana Alloy is not part of this milestone. It may be considered later if Promtail becomes insufficient or deprecated for OWS needs.

## Privacy and Security Notes

Operators must treat observability data as operationally sensitive.

Requirements and warnings:

- do not log raw API keys
- do not log secrets
- do not log package contents
- do not expose student-sensitive operational data publicly
- protect Grafana and Loki with operator-managed auth, reverse proxy, and network controls in real deployments

OWS verifier code already avoids persisting raw API keys in audit and diagnostics output. The observability stack does not change that requirement.

## Troubleshooting

### Prometheus shows target down

Check:

- the base verifier stack is running
- `ows-verifier` is reachable on the shared compose network
- `http://ows-verifier:8080/metrics` works from the Prometheus container

### Grafana dashboard is missing

Check:

- Grafana provisioning directories are mounted
- the dashboard JSON exists
- Grafana container logs do not show provisioning errors

### No logs in Loki

Check:

- Promtail is running
- Promtail can access `/var/run/docker.sock`
- the verifier container name matches `ows-verifier-prod`
- Grafana is querying the `OWS Loki` datasource

### Missing request-count or request-duration panels

That is expected in v0.1. The verifier does not yet expose those metrics on `/metrics`.

### Sensitive data concern

Review verifier request logs, audit events, and operator configuration. If you add reverse proxies or custom log forwarders, ensure they also avoid logging raw API keys or secrets.

## Limitations

This stack is intentionally small.

Known limits:

- optional only; not part of the required base deployment
- no Alertmanager or production alerting
- no Grafana Cloud integration
- no Kubernetes monitoring
- no SSO for Grafana
- no hosted multi-tenant observability service
- no guarantee that Docker-log shipping is the final long-term log ingestion model
