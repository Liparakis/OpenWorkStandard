# Release Readiness — v0.1 Candidate

Status: Candidate only; do not publish automatically.

## Supported today

- .NET 9 CLI and Core local project initialization, Agent observation, package
  creation, offline verification, reports, and reviewer inspection.
- Windows self-contained `Ows.Setup.exe` SCM service; cross-platform foreground Agent host.
- Optional remote verifier sessions, receipts, package intake, and reports.
- Optional canonical package-root hashes and offline RSA signatures.

## Honest limitations

- A student-owned machine remains a weak local trust boundary.
- Linux/macOS service installation is deferred.
- Key rotation/revocation automation is deferred.
- The remote verifier is pilot-grade and never required for local verification.
- OWS does not judge misconduct, detect plagiarism, or prove intent from missing events.

## Validation evidence

- Build: 0 warnings and 0 errors.
- Tests: Core 130/130; CLI/server 80/80.
- Focused package-signing/integrity tests: 11/11.
- Release gate: restore, build, tests, Compose validation, and live pilot dry run passed with PilotTrust=Verified.
- Manual sign-off: pending.

## Supported platforms

- Windows: .NET 9 CLI/Core, self-contained setup executable, silent SCM service, and foreground Agent host.
- Linux/macOS: .NET 9 CLI/Core and foreground Agent host; installable service adapters are deferred.
- Offline verification: supported on any platform that can run the .NET 9 CLI; a verifier server is not required.

## Package and API status

- Package format: `.owspkg` v0.1 with deterministic logical root hashes, optional RSA signatures, and offline verification.
- CLI/API stability: the `ows init` → `ows package` student path and `verify`/`inspect`/`report` reviewer path are the v0.1 candidate surface. Hidden legacy and remote commands remain compatibility-only and may change before a stable release.

## Remaining future work

- Setup/service lifecycle smoke check and manual release sign-off.
- Linux/macOS service adapters, key rotation/revocation automation, and chain-preserving timeline retention/compaction remain deferred.

## Release checks

- LICENSE exists.
- samples/minimal-project exists for onboarding and package smoke checks.
- CHANGELOG.md, SECURITY.md, CONTRIBUTING.md, and GitHub templates exist.
- No publishing, tagging, pushing, or release creation was performed.
- Review repository history and generated artifacts before tagging.

Before release, build and run the Windows setup/service lifecycle smoke check:

~~~powershell
.\scripts\windows\build-ows-setup.ps1
.\artifacts\ows-setup\Ows.Setup.exe
.\artifacts\ows-setup\Ows.Setup.exe --uninstall
~~~

The setup executable requests UAC Administrator approval, installs `OWS Agent`
as LocalSystem, starts it, and removes legacy Scheduled Tasks. The final step
must remove the service and installed files while preserving project `.ows`
folders; the uninstall prompt chooses whether shared registry deletion is also
performed, or `--purge-data` can select it directly.

## Required release commands

~~~text
git diff --check
dotnet build OWS.sln -nologo
dotnet test OWS.sln -nologo
~~~

After manual approval, a maintainer may tag and publish through the repository's
normal GitHub release process. Tagging and pushing are intentionally not
automated by this task.
