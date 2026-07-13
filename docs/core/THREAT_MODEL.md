# OWS Threat Model

## Security goal

OWS makes local project provenance and package integrity tamper-evident. It does not make a student-owned machine tamper-proof.

## Assets

- `.ows/timeline.jsonl`
- recovery snapshot metadata
- package manifest, graph, artifacts, hashes, and optional signature
- project boundary and Agent registration

## In-scope threats

- modifying, reordering, or deleting timeline events
- changing artifacts after packaging
- stopping the Agent and working during an unobserved interval
- changing the initialized-project registry
- reviewers overclaiming what package evidence proves

## Mitigations

- chained event hashes and package hash validation
- canonical package-root hashing independent of ZIP order and timestamps
- optional offline RSA signatures
- explicit project initialization and bounded filesystem observation
- recovery snapshots and neutral continuity findings
- binary files represented only by metadata and hashes

## Out of scope

- endpoint compromise, keyloggers, spyware, or device lockdown
- automated anti-cheat judgment
- institutional identity, LMS records, grading, or hosted management

## Honest claims

OWS can establish whether a package is internally consistent and, when signed, whether its embedded public key validates the package root. It cannot prove that every edit was observed or explain why an observation gap occurred.
