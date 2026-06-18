# Security Model

## Threat model

The initial threat model assumes attempts such as:

- modifying package contents after creation
- fabricating history
- corrupting local evidence
- deleting intermediate provenance
- manipulating timestamps

## Tamper detection

OWS plans to rely on hash-based integrity checks across events, deltas, or snapshots and the Work Version Graph. A verification failure should say exactly what could not be trusted.

## Hash chain design

The current codebase includes SHA-256 hashing primitives in `Ows.Core`. The intended design is to chain version state through parent-aware hashes so later verification can detect breaks in continuity.

## Digital signature plan

`signature.json` is reserved for future signing support. The likely direction is detached signatures over package content and manifest identity, but this is still design-level work, not implemented functionality.

## Package verification

Verification should eventually check:

- required package entries exist
- manifest structure is valid
- timeline is readable
- graph references are coherent
- hashes match expected values
- reconstruction can proceed without contradictions

## Known limitations

- package creation is not implemented yet
- package verification is not implemented yet
- digital signatures are not implemented yet
- timestamp trust is limited until a formal signing or timestamp strategy exists

The current repository should be treated as a foundation, not as a finished security system.
