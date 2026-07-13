# Review Guidance

Reviewers can use OWS without a server:

1. Receive the `.owspkg` file.
2. Run `ows verify path/to/submission.owspkg`.
3. Run `ows inspect path/to/submission.owspkg` for manifest, artifact, timeline, and integrity details.
4. Run `ows report path/to/submission.owspkg --format text` or `--format json` when a saved report is needed.

`Verified` means the local package signature and integrity checks align. `Unverified` means the package is structurally valid but unsigned. `Degraded` means a signed package records an observation continuity issue. `Invalid` means the package or its evidence is inconsistent.

OWS reports evidence and uncertainty. It does not decide misconduct, and missing events are never proof of misconduct.
