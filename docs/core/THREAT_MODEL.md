Status: Reference  
Audience: Security reviewer, Developer, Operator  
Last reviewed: 2026-06-20

# OWS Threat Model

## Scope

This document describes the current Open Work Standard threat model for the MVP repository.

It is intentionally narrow:

- local project capture
- `.owspkg` package creation
- local and remote checkpoint issuance
- package verification
- local verifier development flow

It does not pretend to cover institutional production deployment in full.

## Security Goal

OWS aims to make academic work provenance tamper-evident.

OWS does not aim to make a student-owned machine tamper-proof.

## Primary Assets

- local timeline history in `.ows/timeline.jsonl`
- local session metadata in `.ows/session.json`
- local observed recovery snapshot in `.ows/observed_snapshot.json`
- local receipt material in `.ows/receipts.json`
- packaged artifacts inside `.owspkg`
- package manifest, timeline, and version graph hashes
- canonical package-root hash and optional public-key signature
- verifier-issued receipt chains
- durable verifier checkpoint history in PostgreSQL

## Trust Boundaries

### Untrusted or weakly trusted

- student-owned workstation
- local filesystem outside cryptographic validation
- local long-running watcher process, including the Windows LocalSystem SCM Agent
- local verifier development environment

### Stronger trust boundary

- durable remote verifier storage
- verifier-issued receipt history after durable commit

## Threat Actors

- a student modifying local evidence after the fact
- a student stopping local capture and continuing off-record
- a local operator misconfiguring or disabling verifier infrastructure
- an external client replaying or duplicating checkpoint requests
- an institution overclaiming what the current MVP can prove

## In-Scope Threats

- modifying timeline events after capture
- deleting, duplicating, or reordering local events
- modifying packaged artifacts after packaging
- forging or replaying checkpoint requests
- retry storms and accidental duplicate submissions
- stale or missing local verifier state during development
- verifier storage outages or migration failures
- trust overstatement when receipts are missing or incomplete
- unauthorized expansion of the Windows Agent's watched roots through registry tampering

## Out-of-Scope Threats

- full endpoint compromise on the student machine
- keylogger or spyware already present on the workstation
- side-channel resistance
- anti-cheat or device lockdown
- human collusion outside the protocol
- final institutional policy decisions

## Current Mitigations

- chained local timeline events in `timeline.jsonl`
- package manifest hashing
- canonical package-root hashing independent of ZIP entry ordering and timestamps
- optional offline RSA package signatures with public key and fingerprint in `signature.json`
- user-local private key storage with restrictive Unix mode and Windows current-user DPAPI protection
- artifact hash verification
- packaged receipt-chain verification
- live verifier cross-checking
- trust grading with `Verified`, `Unverified`, and `Invalid`
- `Degraded` trust state for session continuity and observation gaps
- observation gap and large unobserved change detection during watcher recovery
- snapshot-hash commitments in the timeline via `SnapshotUpdated`
- recovery baseline degradation when `observed_snapshot.json` is missing, corrupt, unbound, or mismatched
- durable PostgreSQL-backed verifier storage
- idempotent checkpoint retry handling
- app-owned verifier migrations
- built-in per-endpoint rate limiting for public probes, auth management, package uploads, session writes, and diagnostics reads
- dedicated rate limiting for verifier writes, uploads, and diagnostics
- multipart body length enforcement for package uploads
- upload authorization checks before blob persistence
- archive entry-count, path, duplicate-entry, expansion-size, and compression-ratio checks before package blobs are accepted
- audit coverage for verifier writes, package verification, and scoped reads

## Known Gaps

- the verifier is not yet production-grade
- auth and RBAC are implemented for the current verifier roles, but still API-first and pilot-grade
- signing-key custody is local-user scoped; key rotation and revocation remain manual
- unsigned packages remain structurally valid and must be treated as weaker than signed packages
- retention enforcement is not implemented
- a well-formed local snapshot can still be rewritten if an attacker also rewrites the local timeline and no stronger remote anchor exists
- anonymous `/ready` and `/metrics` remain intentionally public and therefore rely on reverse-proxy network controls plus rate limiting, not authentication

## What OWS Can Honestly Claim Today

- package and event integrity can be checked
- snapshot baseline continuity can be checked when `SnapshotUpdated` commitments are present
- receipt chains can be validated
- local evidence can be tamper-evident
- signed packages can be verified offline against their embedded public key
- remote durable receipts improve trust

## What OWS Must Not Claim Today

- that it prevents all cheating
- that local capture cannot be disabled
- that local-only evidence is as strong as durable remote receipts
- that the current verifier is already an institutional-grade trust boundary

## Roadmap Consequence

The next security-relevant implementation priorities remain:

1. stronger watcher lifecycle and capture fidelity
2. durable and operationally reliable verifier flow
3. server-side package submission and verification
4. production-grade deployment controls (reverse proxy, network policy, TLS termination, managed secrets, and external object storage) after the current pilot flow is stable
