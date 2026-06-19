# Open Work Standard

Open Work Standard (OWS) is an open assessment provenance and notarization protocol. Its purpose is to capture the evolution of coursework, package that evidence into `.owspkg` archives, and support verification against local integrity signals and, later, remote verifier receipts.

OWS is not an AI detector, proctoring tool, surveillance product, or automated misconduct judge. It is provenance and notarization infrastructure for preserving work history and supporting human review.

## MVP goals

- Establish the core OWS domain model and terminology.
- Provide a CLI entry point for `init`, `watch`, `package`, `verify`, and `report`.
- Define the local `.ows/` storage model and `.owspkg` package contract.
- Keep local capture simple while preparing for remote trust boundaries.
- Produce a testable, documented .NET solution that can grow into the full reference implementation.

## Quick start

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Ows.Cli -- --help
```

## Repository structure

- `src/Ows.Core`: shared domain models plus agent, packaging, notarization, verification, and reporting namespaces.
- `src/Ows.Cli`: command-line entry point for the reference client.
- `src/Ows.Desktop`: placeholder project for a future Avalonia UI.
- `src/Ows.Verifier.Server`: minimal ASP.NET Core verifier API scaffold backed by a local JSON receipt snapshot.
- `tests/Ows.Core.Tests`: xUnit coverage for core behavior and collapsed MVP service skeletons.
- `tests/Ows.Cli.Tests`: xUnit coverage for command construction.
- `docs`: specification, architecture, privacy, security, package format, CLI, and glossary.
- `docs/THREAT_MODEL.md`: explicit MVP threat model and trust-boundary limits.
- `docs/REVIEW_GUIDANCE.md`: how reviewers should interpret trust states, findings, and signals.
- `docs/DEFERRED_NOTES.md`: explicit "not yet" decisions and deferred follow-up items.
- `samples/sample-project`: tiny sample tree used for demos and future integration tests.

## Development setup

- SDK target: `.NET 9`
- CLI package: `System.CommandLine`
- Hosting/logging: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging`
- Local storage dependency planned for the agent: `Microsoft.Data.Sqlite`
- Test stack: `xUnit`, `FluentAssertions`

The repository includes a local `NuGet.Config` so it does not inherit broken machine-level package sources.

## Current status

This repository now has a thin but real local MVP:

- `ows init` creates local `.ows` state
- `ows watch` performs a one-shot project scan
- `ows session start` and `ows session checkpoint` work locally or against a configured verifier API
- `ows package` creates real `.owspkg` archives
- `ows verify` validates package integrity and can cross-check packaged receipts against a live verifier API
- `ows report` writes a text or JSON integrity report with findings and review signals

The main architectural gap is the durable trust boundary. Local capture alone is not enough, so the next milestone is turning the current in-memory verifier scaffold into a self-hostable service with durable storage and hardened transport.

## Verifier schema setup

The Work Verifier owns its PostgreSQL schema lifecycle.

For local development or self-hosted bootstrap, run:

```bash
dotnet run --project src/Ows.Verifier.Server -- migrate
```

Normal PostgreSQL-backed verifier startup also applies missing ordered migrations automatically. DevOps still owns the database instance, credentials, backups, and deployment policy; OWS owns the verifier schema shape.

## Local durable verifier

For the smallest real PostgreSQL-backed local flow:

```bash
docker compose -f docker-compose.local.yml up -d
```

Then follow [VERIFIER_LOCAL_DEV.md](/C:/Users/Liparakis/Desktop/Open%20Work%20Standard/docs/VERIFIER_LOCAL_DEV.md).

Fastest local path on Windows:

```powershell
.\scripts\run-local-verifier.ps1
```

Then smoke-test the verifier directly with:

```powershell
.\scripts\test-local-verifier.ps1
```

`dotnet build` also emits platform-specific verifier helper scripts under `artifacts/generated-scripts/`.

For background local lifecycle on Windows:

```powershell
.\scripts\start-local-verifier.ps1
.\scripts\status-local-verifier.ps1
.\scripts\logs-local-verifier.ps1
.\scripts\test-local-verifier.ps1
.\scripts\stop-local-verifier.ps1
```
