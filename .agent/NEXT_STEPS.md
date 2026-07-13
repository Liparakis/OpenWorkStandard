# Next Steps

1. Inspect the final diff and active Markdown links; remove any remaining stale remote/session wording.
2. Clean ignored build/test outputs and verify the reduced root surface.
3. Commit the completed legacy-removal slice.
4. Owner reviews the reduced repository, confirms the release candidate, and explicitly authorizes publication.
5. Only after approval: tag the selected version, push the tag, and create the GitHub release.

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
