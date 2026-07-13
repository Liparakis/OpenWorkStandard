# Next Steps

1. Owner action required: run the current `artifacts\ows-setup\Ows.Setup.exe` with UAC approval to replace the older installed payload.
2. Confirm `OWS Agent` remains Running/Automatic/LocalSystem and points to the current payload.
3. Use Add/Remove Programs to remove the service, installed payload, and uninstall entry; choose shared-data deletion only when intended.
4. Owner reviews repository history, license, generated artifacts, and current worktree scope.
5. Only after approval: tag the selected version, push the tag, and create the GitHub release.

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
