# CLI Reference

The CLI is intentionally small and local-first.

## Commands

- `ows init`: initialize the current project and register its explicit root.
- `ows status`: show local initialization and Agent state.
- `ows package [--output path] [--sign]`: create a `.owspkg` package.
- `ows verify [package]`: verify a package offline.
- `ows inspect [package]`: inspect package metadata and findings.
- `ows report [package] [--format text|json]`: write a reviewer report beside the package.

`ows init`, `ows status`, and `ows package` support `--json` for structured project-state output. `ows inspect --json` provides structured package inspection; `ows report --format json` writes a JSON report. `ows init` and `ows package` are the normal student path. No session, upload, checkpoint, server, or credential ceremony is required.
