# Start Here

Status: Active  
Audience: Student, Reviewer, Operator, Developer, Security reviewer  
Last reviewed: 2026-06-20

Open Work Standard (OWS) is local-first academic work provenance and notarization infrastructure. It records project-scoped evidence, packages it into `.owspkg`, and supports later verification and human review.

Core invariant:
Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

OWS does not run automated misconduct judgment or AI cheating detection. Unobserved large changes are evidence continuity review signals, not accusations of misconduct or cheating.

## I am a student

- [Student Workflow](workflows/STUDENT_WORKFLOW.md)
- [Local Demo](workflows/LOCAL_DEMO.md)
- [CLI Reference](development/CLI.md)
- [Getting Started](../GETTING_STARTED.md)

## I am a professor or reviewer

- [Review Guidance](workflows/REVIEW_GUIDANCE.md)
- [Pilot Demo](workflows/PILOT_DEMO.md)
- [Package Format](core/PACKAGE_FORMAT.md)
- [Local Inspect](../CLI_REFERENCE.md)
- [Event Catalog](core/EVENT_CATALOG.md)

## I am an institution operator or admin

- [Self-Hosting Guide](operations/SELF_HOSTING.md)
- [Docker Compose Self-Hosting](operations/SELF_HOSTED_COMPOSE.md)
- [Operations Runbook](operations/OPERATIONS_RUNBOOK.md)
- [Backup and Restore](operations/BACKUP_RESTORE.md)
- [Troubleshooting](operations/TROUBLESHOOTING.md)
- [Production Readiness](operations/PRODUCTION_READINESS.md)

## I am a developer or contributor

- [Core Overview](core/OVERVIEW.md)
- [Architecture](core/ARCHITECTURE.md)
- [Agent Design](AGENT_DESIGN.md)
- [API Specification](development/API.md)
- [Roadmap Checklist](development/ROADMAP_CHECKLIST.md)
- [Release Checklist](development/RELEASE_CHECKLIST.md)

## I am reviewing privacy or security

- [Threat Model](core/THREAT_MODEL.md)
- [Privacy Boundaries](core/PRIVACY.md)
- [Security Model](core/SECURITY.md)
- [Security Hardening](operations/SECURITY_HARDENING.md)
- [Security Channels](reference/SECURITY_CHANNELS.md)

## I want to run the local demo

- [Local Demo](workflows/LOCAL_DEMO.md)
- [Pilot Demo](workflows/PILOT_DEMO.md)
- [Local Verifier Dev](development/VERIFIER_LOCAL_DEV.md)

## I want to understand the protocol

- [Core Overview](core/OVERVIEW.md)
- [Architecture](core/ARCHITECTURE.md)
- [Package Format](core/PACKAGE_FORMAT.md)
- [Event Schema](core/EVENT_SCHEMA.md)
- [Event Catalog](core/EVENT_CATALOG.md)
- [Threat Model](core/THREAT_MODEL.md)
