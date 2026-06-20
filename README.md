# Open Work Standard

Open Work Standard (OWS) is local-first academic work provenance and notarization infrastructure. It records project-scoped evidence, packages that evidence into `.owspkg`, and supports later verification and human review without claiming automated misconduct judgment.

OWS does:
- record project-scoped work evidence
- package evidence into `.owspkg`
- verify package hashes, timeline integrity, and optional remote receipts
- surface neutral review signals for continuity gaps

OWS does not:
- run AI cheating detection
- make automated misconduct decisions
- treat missing events as proof of misconduct
- collect keylogging, webcam, microphone, or unrelated personal data

Core invariant:
Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

Quick demo path:
1. `dotnet build OWS.sln -nologo`
2. `dotnet test OWS.sln -nologo`
3. Follow [docs/START_HERE.md](docs/START_HERE.md)
4. For a walkthrough, use [docs/workflows/LOCAL_DEMO.md](docs/workflows/LOCAL_DEMO.md)

Current status:
- MVP reference flow is real
- optional remote verifier path exists
- observation gaps degrade continuity rather than implying misconduct
- the semantic Work Version Graph is still scaffolded/deferred

Documentation entry point:
- [Start Here](docs/START_HERE.md)

Key references:
- [Review Guidance](docs/workflows/REVIEW_GUIDANCE.md)
- [Threat Model](docs/core/THREAT_MODEL.md)
- [Project Status](docs/development/PROJECT_STATUS.md)

License:
- [LICENSE](LICENSE)
