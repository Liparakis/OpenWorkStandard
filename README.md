# Open Work Standard

Open Work Standard (OWS) is local-first academic work provenance and notarization infrastructure. It records project-scoped evidence, packages that evidence into `.owspkg`, and supports later verification and human review without claiming automated misconduct judgment.

OWS does:
- record project-scoped work evidence
- initialize a project-scoped `.owsignore` for safe local exclusions
- register explicitly initialized projects with the local OWS Agent
- package evidence into `.owspkg`
- optionally seal packages with a canonical root hash and offline RSA signature
- verify package hashes, timeline integrity, and optional remote receipts
- surface neutral review signals for continuity gaps
- preserve optional opaque external context identifiers without owning institutional records

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
3. Follow [docs/START_HERE.md](docs/START_HERE.md)
4. For a walkthrough, use [docs/workflows/LOCAL_DEMO.md](docs/workflows/LOCAL_DEMO.md)

Windows setup:
- Run `scripts/windows/build-ows-setup.ps1`, then double-click `artifacts/ows-setup/Ows.Setup.exe`.
- UAC approval installs the silent `OWS Agent` SCM service and registers Open Work Standard in Installed apps.
- Uninstall from Installed apps; choose whether to remove shared Agent data. Project `.ows` folders are never removed.

Current status:
- MVP reference flow is real
- optional remote verifier path exists
- observation gaps degrade continuity rather than implying misconduct
- verifier v0.1 now includes built-in rate limiting, scoped upload rejection before blob persistence, and stricter `.owspkg` archive admission checks
- the semantic Work Version Graph is still scaffolded/deferred

Documentation entry point:
- [Start Here](docs/START_HERE.md)
- [Getting Started](GETTING_STARTED.md)
- [CLI Reference](CLI_REFERENCE.md)
- [Security](SECURITY.md)
- [Architecture](ARCHITECTURE.md)
- [Package Format](PACKAGE_FORMAT.md)
- [Threat Model](THREAT_MODEL.md)
- [Contributing](CONTRIBUTING.md)

Key references:
- [Review Guidance](docs/workflows/REVIEW_GUIDANCE.md)
- [Project Specification](SPEC.md)
- [Project Status](docs/development/PROJECT_STATUS.md)

License:
- [LICENSE](LICENSE)
