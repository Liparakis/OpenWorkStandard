# OWS Package Format

The target package layout is:

```text
submission.owspkg
├─ manifest.json
├─ timeline.jsonl
├─ version_graph.json
├─ artifacts/
├─ deltas/
├─ metadata/
└─ signature.json
```

## Purpose

The package captures enough provenance to verify work evolution later without depending on the original local machine.

## Entries

- `manifest.json`: package metadata, toolchain, identifiers, and generation context.
- `timeline.jsonl`: append-friendly chronological event stream.
- `version_graph.json`: graph structure for version reconstruction and integrity checks.
- `artifacts/`: final or intermediate project artifacts chosen for submission or inspection.
- `deltas/`: change units or snapshots used to reconstruct work history.
- `metadata/`: auxiliary information that does not fit the main manifest.
- `signature.json`: reserved for future digital signature support.

## Notes

- `.owspkg` names the OWS package format and replaces any older `.oapkg` terminology.
- The current repository implements real package assembly for `manifest.json`, `timeline.jsonl`, `version_graph.json`, and `artifacts/`.
- Report output is intentionally external to this initial minimal package shape so the submission artifact stays focused on provenance data.
