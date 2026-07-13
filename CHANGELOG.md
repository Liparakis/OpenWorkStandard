# Changelog

## Unreleased — v0.1 release candidate

### Added

- Local project registry and Windows-first always-on Agent host.
- Current-user local IPC and a self-contained `Ows.Setup.exe` Windows SCM service installer.
- Shared .owsignore tracking and packaging exclusions.
- Canonical package-root hashes and optional offline RSA signatures.
- Reviewer-focused local ows inspect command.
- Contributor, security, release, and issue-reporting entry points.

### Changed

- OWS is explicitly local-first, text-first, IDE-agnostic, and not an LMS.
- Student documentation prioritizes ows init followed by ows package.
- Education-management and active IDE integration code was removed from OWS scope.

### Known limitations

- Linux/macOS installable Agent service adapters are deferred.
- Local private-key rotation/revocation automation is deferred.
- The verifier server remains pilot-grade and optional for local verification.
- Manual release sign-off is still required.
