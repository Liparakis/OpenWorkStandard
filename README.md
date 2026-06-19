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
- `ows verify` validates package integrity and assigns a trust grade
- `ows report` writes a basic text integrity report

The main architectural gap is the durable trust boundary. Local capture alone is not enough, so the next milestone is turning the current in-memory verifier scaffold into a self-hostable service with durable storage and hardened transport.
