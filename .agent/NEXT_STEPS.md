# Next Steps

1. Resolve or review the unrelated `Ows.Core.Packaging.Helpers` compile blocker in the current working tree, then rerun Core/full tests and Release build.
2. Owner reviews commits `6001de1`, `8f9fc3b`, `6856367`, `6d7b7e6`, and the new watcher/XML cleanup commit, plus history, license, and manual Windows Agent lifecycle evidence.
3. Decide separately whether the existing Agent/scanning/watcher/packaging working-tree changes should be retained and committed.
4. Owner confirms the local tamper-detection boundary and release candidate.
5. Only after explicit approval: tag, push, and publish; no publication is currently authorized.

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
