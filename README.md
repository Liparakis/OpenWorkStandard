# Open Work Standard

Open Work Standard (OWS) is a local-first, cross-platform academic work provenance system. Its purpose is to capture the evolution of coursework, preserve evidence locally, package that evidence into `.owspkg` archives, and verify those archives later.

OWS is not an AI detector, proctoring tool, surveillance product, or automated misconduct judge. It is a provenance standard and reference implementation for preserving work history and supporting human review.

## MVP goals

- Establish the core OWS domain model and terminology.
- Provide a CLI entry point for `init`, `watch`, `package`, `verify`, and `report`.
- Define the local `.ows/` storage model and `.owspkg` package contract.
- Keep all captured evidence local by default.
- Produce a testable, documented .NET solution that can grow into the full reference implementation.

## Quick start

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Ows.Cli -- --help
```

## Repository structure

- `src/Ows.Core`: shared domain models, hashing, graph primitives, constants, manifest types.
- `src/Ows.Cli`: command-line entry point and placeholder commands.
- `src/Ows.Agent`: local tracking and file watching skeleton.
- `src/Ows.Packaging`: `.owspkg` creation skeleton.
- `src/Ows.Verification`: package verification skeleton.
- `src/Ows.Reporting`: JSON/text/HTML report generation skeleton.
- `src/Ows.Desktop`: placeholder project for a future Avalonia UI.
- `tests/*`: xUnit test projects covering current behavior.
- `docs`: specification, architecture, privacy, security, package format, CLI, and glossary.
- `samples/sample-project`: tiny sample tree used for demos and future integration tests.

## Development setup

- SDK target: `.NET 9`
- CLI package: `System.CommandLine`
- Hosting/logging: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging`
- Local storage dependency planned for the agent: `Microsoft.Data.Sqlite`
- Test stack: `xUnit`, `FluentAssertions`

The repository includes a local `NuGet.Config` so it does not inherit broken machine-level package sources.

## Current status

This repository is intentionally at the solution-bootstrap stage. The domain model, documentation, CI, and tests are in place. The command handlers and service projects are honest skeletons that compile and clearly report `not implemented yet` rather than pretending to provide tracking, packaging, verification, or reporting behavior that does not exist yet.
