# Open Work Standard

Open Work Standard (OWS) is a local-first proof-of-work protocol and toolchain for academic projects. It records project-scoped evidence, packages that evidence into `.owspkg`, and supports offline verification and human review without claiming automated misconduct judgment.

OWS does:
- record project-scoped work evidence
- initialize a project-scoped `.owsignore` for safe local exclusions
- register explicitly initialized projects with the local OWS Agent
- package evidence into `.owspkg`
- optionally seal packages with a canonical root hash and offline RSA signature
- verify package hashes, timeline integrity, and optional offline signatures
- surface neutral review signals for continuity gaps

OWS does not:
- run AI cheating detection
- make automated misconduct decisions
- treat missing events as proof of misconduct
- collect keylogging, webcam, microphone, or unrelated personal data
- act as an LMS or manage courses, rosters, students, or assessments

Core invariant:
Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

Quick demo path:
1. `dotnet build OWS.sln -nologo`
2. `dotnet test OWS.sln -nologo`
3. Install the Windows Agent with `artifacts/ows-setup/Ows.Setup.exe`.
4. In a project directory, run `ows init`.
5. Work normally, then run `ows package` and `ows verify <package>`.

Windows setup:
- Run `scripts/windows/build-ows-setup.ps1`, then double-click `artifacts/ows-setup/Ows.Setup.exe`.
- UAC approval installs the silent `OWS Agent` SCM service, installs the `ows` CLI, and registers Open Work Standard in Installed apps.
- Uninstall from Installed apps; choose whether to remove shared Agent data. Project `.ows` folders are never removed.

Current status:
- MVP reference flow is real
- observation gaps degrade continuity rather than implying misconduct
- the semantic Work Version Graph is still scaffolded/deferred

CLI help:
- Run `ows --help` for the available commands and options.
- Run `ows <command> --help` for command-specific help.

License:
- [LICENSE](LICENSE)
