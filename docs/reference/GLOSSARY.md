# Glossary

- `Open Work Standard`: the provenance standard and reference implementation described in this repository.
- `Work provenance`: evidence about how work evolved over time, not only the final output.
- `Work Version Graph`: the directed acyclic graph that models work states and transitions.
- `Review Signal`: a neutral indicator that may justify human review without implying guilt.
- `Work Verifier`: the component or workflow that validates an OWS package.
- `.ows`: the local project-scoped evidence directory.
- `.owspkg`: the portable OWS submission package format.
- `Manifest`: the package metadata document, typically `manifest.json`.
- `Timeline`: the chronological event stream, typically `timeline.jsonl`.
- `Delta`: a stored change unit between work states.
- `Snapshot`: a stored state of project contents at a point in time.
- `Hash chain`: a sequence of hashes that helps detect tampering or discontinuities.
- `Package root`: the canonical logical byte representation of package metadata and content hashes, independent of ZIP entry ordering.
- `Package signature`: an optional public-key signature over the canonical package root for offline authenticity checks.
- `Signature status`: the package-level state `Valid`, `Unsigned`, `UnsignedLegacy`, or `Invalid` reported by offline verification.
