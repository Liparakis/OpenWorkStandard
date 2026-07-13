# Next Steps

1. Run `artifacts/ows-setup/Ows.Setup.exe` with UAC approval to replace the installed Agent service.
2. In a fresh project directory, run `ows init` and confirm it no longer reports Access denied.
3. Confirm `Get-Service OwsAgent` is Running and the service responds to the user CLI.
4. Keep the current changes unstaged unless the owner explicitly requests a commit.

Current phase remaining:

- End-to-end validation requires installing the rebuilt service on the machine.

Next roadmap phase:

- Resume the normal product roadmap after the Windows bootstrap smoke test.

Prerequisites for the next phase:

- Updated `OwsAgent.exe` installed and `ows init` verified from a new project directory.

Deferred:

- StyleCop formatting/style diagnostics, hosted verification or tamper anchoring, desktop UI, IDE adapters, management layers, signing-key rotation/revocation automation, and chain-preserving timeline retention/compaction.

Owner review:

- Confirm the rebuilt setup replaces the service and `ows init` succeeds as the interactive user.
- Confirm setup extracts the payload and Services.msc points to the extracted executable.
