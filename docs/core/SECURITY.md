# Security Model

OWS is tamper-evident, not tamper-proof on a student-owned machine.

## Protections

- chained local timeline events
- SHA-256 hashes for timelines, graphs, artifacts, and package roots
- optional offline RSA package signatures with embedded public key and fingerprint
- opaque, hash-only binary artifacts
- explicit initialized-project boundaries
- neutral observation-gap and recovery findings
- Windows Agent installation behind UAC and the Service Control Manager

## Limits

An attacker who controls the workstation may stop the Agent or rewrite local evidence. A valid unsigned package is structurally usable but does not establish package authenticity. OWS does not prevent cheating, identify misconduct, or replace human review.

Private signing keys remain user-local. Key rotation and revocation are deferred. Hosted verification, institutional identity, and management systems are outside this repository.
