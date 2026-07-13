# OWS Package Format

## Scope

This document defines the current MVP `.owspkg` package format as implemented by this repository.

It is a package format specification for the current codebase, not a future wishlist.

## Container

- extension: `.owspkg`
- container type: ZIP archive
- path separator inside the archive: `/`

## Required Entries

Every valid MVP package must contain:

- `manifest.json`
- `timeline.jsonl`
- `version_graph.json`

## Optional Entries

A package may also contain:

- `session.json`
- `receipts.json`
- `artifacts/...`

## Current Archive Shape

Typical current package layout:

```text
submission.owspkg
|- manifest.json
|- timeline.jsonl
|- version_graph.json
|- session.json              (optional)
|- receipts.json             (optional)
|- signature.json            (optional)
`- artifacts/
```

## Entry Semantics

### `manifest.json`

The manifest currently carries:

- `owsVersion`
- `generatedAtUtc`
- `packageId`
- `projectName`
- `platform`
- `toolchain`
- `trackedPath`
- `timelineHash`
- `versionGraphHash`
- `sessionStateHash`
- `artifactHashes`
- `packageRootHash`
- `receiptChainHash`
- `signatureAlgorithm` and `signatureKeyFingerprint` when signed

Current behavior:

- `timelineHash` is the SHA-256 hash of the packaged `timeline.jsonl`
- `versionGraphHash` is the SHA-256 hash of the packaged `version_graph.json`
- `sessionStateHash` is empty when `session.json` is absent
- `artifactHashes` maps archive paths such as `artifacts/src/Program.cs` to SHA-256 content hashes
- `packageRootHash` is the SHA-256 hash of the canonical logical root bytes
- `receiptChainHash` is the SHA-256 hash of `receipts.json` when present
- signature fields identify the optional public-key signature

### `timeline.jsonl`

- canonical local event stream
- each line is one serialized `OwsEvent`
- event chaining rules are defined in `docs/core/EVENT_SCHEMA.md`

### `version_graph.json`

- current MVP placeholder graph document
- current emitted content is an empty graph: `{"nodes":[],"edges":[]}`
- this is still part of the package contract and is hash-checked

### `session.json`

- optional packaged session metadata
- used to resolve a remote verifier session during verification when present

### `receipts.json`

- optional packaged receipt-chain snapshot
- used for packaged receipt verification and optional live verifier cross-checking

### `artifacts/...`

- packaged project files outside `.ows/` and the paths excluded by `.owsignore`
- the output `.owspkg` itself is excluded
- archive paths are rooted under `artifacts/`
- explicitly included binary files are opaque and represented by their path and hash

### `signature.json`

- optional RSA-SHA256-PKCS1-v1_5 signature over the canonical logical package-root bytes
- contains the root hash, public key, key fingerprint, algorithm, and Base64 signature
- contains no private key material

### Canonical package root

The root is independent of ZIP entry ordering and ZIP timestamps. The current
`OWS-PACKAGE-ROOT-V1` canonicalizer
serializes the manifest with root/signature self-reference fields blank, then
appends sorted logical content-hash lines for the timeline, version graph,
session, receipts, and artifacts using UTF-8 LF bytes under the
`OWS-PACKAGE-ROOT-V1` format marker.

## Exclusions

The current package builder excludes:

- files under `.ows/`
- paths matched by the shared `.owsignore` rules, including common build, dependency, secret, and log paths
- the output package file itself

## Reserved But Not Implemented

These are not part of the implemented MVP package format today:

- `deltas/`
- `metadata/`

Do not treat those as present or required until the code actually emits and verifies them.

## Verification Rules

Current package verification checks:

- required package entries exist
- `manifest.json` is valid JSON
- `timeline.jsonl` is valid and its event chain recomputes correctly
- `version_graph.json` is valid JSON
- manifest hashes match packaged timeline, version graph, session state, and artifact contents
- packaged receipt chains verify when present
- live verifier cross-checking can be performed when a verifier URL is supplied
- canonical package-root hashes are checked when present
- signed packages are verified offline; unsigned packages remain valid with an explicit unsigned signature state

## Compatibility Rule

If the implemented package shape changes:

- update this document in the same commit
- update `docs/development/ROADMAP_CHECKLIST.md` if capability status changes
- keep `ows verify` aligned with the documented required and optional entries

