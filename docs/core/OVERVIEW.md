# OWS Overview

Status: Active  
Audience: Student, Reviewer, Operator, Developer, Security reviewer  
Last reviewed: 2026-06-20

Open Work Standard (OWS) is a local-first academic work provenance and notarization system. It records project-scoped evidence, packages that evidence into `.owspkg`, and supports verification against local integrity signals and optional remote verifier receipts.

OWS is not an AI detector, proctoring system, automated misconduct judge, or surveillance product. It does not claim to prove intent. It is infrastructure for preserving work history and supporting human review.

Core invariant:
Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

Unobserved large changes are evidence continuity review signals, not accusations of misconduct or cheating.

## What OWS does

- Captures project-scoped file and workflow evidence in `.ows/`.
- Hash-chains timeline events for tamper evidence.
- Commits observed snapshot hashes into the timeline for recovery-baseline checking.
- Packages current project evidence into `.owspkg`.
- Verifies package structure, hashes, timeline integrity, and optional remote receipts.
- Produces reviewer-facing reports that distinguish current package integrity from observation continuity.

## What OWS does not do

- It does not claim to prevent all cheating.
- It does not run AI cheating detection.
- It does not make automated misconduct judgments.
- It does not collect raw keystrokes, private messages, browser history, webcam data, or microphone data.
- It does not treat an observation gap as proof of misconduct.

## Reading path

- [Architecture](ARCHITECTURE.md)
- [Package Format](PACKAGE_FORMAT.md)
- [Event Catalog](EVENT_CATALOG.md)
- [Threat Model](THREAT_MODEL.md)
- [Privacy Boundaries](PRIVACY.md)
- [Security Model](SECURITY.md)
