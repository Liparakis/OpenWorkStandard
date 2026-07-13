# Next Steps

1. Owner reviews commits `6001de1`, `8f9fc3b`, and `6856367`, plus the history, license, and manual Windows Agent lifecycle evidence.
2. Decide whether the separate uncommitted `src/Ows.Cli/OwsCommandFactory.cs` edit should be retained and committed; it is not part of the dead-field fix.
3. Owner confirms the local tamper-detection boundary and release candidate.
4. Only after explicit approval: tag, push, and publish; no publication is currently authorized.

Current phase remaining:

- Owner review of the local tamper-detection boundary and SCM setup lifecycle.

Next roadmap phase:

- None until owner sign-off; future work begins only after this release candidate is accepted.

Prerequisites for the next phase:

- Explicit owner approval of the current local-first contract.
- A separately scoped decision for any hosted anchoring or remote review project.

Deferred:

- Hosted verification or tamper anchoring, desktop UI, IDE adapters, management layers, signing-key rotation/revocation automation, and chain-preserving timeline retention/compaction.

Owner review:

- Confirm that local timeline chaining, artifact/package-root hashes, and optional offline signatures are sufficient for v0 tamper detection.
- Confirm that the SCM setup/uninstall lifecycle and shared Agent-data preservation choice match the intended Windows experience.
