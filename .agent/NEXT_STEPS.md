# Next Steps

1. Remove the remaining session/receipt/remote-verifier implementation and its tests, scripts, and docs; preserve local timeline/package verification.
2. Run the full build/test and inspect the reduced CLI/package surface.
3. Owner reviews the reduced repository, confirms the release candidate, and explicitly authorizes publication.
4. Only after approval: tag the selected version, push the tag, and create the GitHub release.

Post-release roadmap:

- Linux/macOS installable Agent service adapters.
- Signing-key rotation/revocation automation.
- Further reduction of optional remote/diagnostic command surface after pilot dependency review.

Deferred:

- Hosted key management, desktop UI, IDE adapters, management layers, and automatic misconduct judgment.
- Chain-preserving timeline retention/compaction design for projects that outlive the v0.1 package format.

Owner review:

- Confirm package format compatibility and the decision to keep signatures opt-in.
- Confirm the LocalSystem service can read the machine-scoped explicit-project registry and that uninstall removes only installed/service files when the prompt selects preservation, unless `--purge-data` is explicit.
