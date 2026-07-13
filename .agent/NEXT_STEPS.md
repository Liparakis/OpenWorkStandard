# Next Steps

1. Owner reviews commit `ca9152e`, the reduced root/docs surface, history, license, and manual Windows lifecycle evidence.
2. Owner confirms the release candidate and explicitly authorizes publication.
3. Only after approval: tag the selected version, push the tag, and create the GitHub release.

Post-release roadmap:

- Linux/macOS installable Agent service adapters.
- Signing-key rotation/revocation automation.
- Further reduction of optional diagnostic surface after owner review.

Deferred:

- Hosted verification/key management, desktop UI, IDE adapters, management layers, and automatic misconduct judgment.
- Chain-preserving timeline retention/compaction design for projects that outlive the v0.1 package format.

Owner review:

- Confirm package format and the decision to keep signatures opt-in.
- Confirm the LocalSystem service can read the machine-scoped explicit-project registry and that uninstall removes only installed/service files when the prompt selects preservation, unless `--purge-data` is explicit.
