# CLI Reference

The CLI is intentionally small and local-first.

## Commands

- `ows init`: initialize the current project and register its explicit root.
- `ows agent run`: run the local Agent host.
- `ows status`: show local initialization and Agent state.
- `ows package [--output path] [--sign]`: create a `.owspkg` package.
- `ows verify [package]`: verify a package offline.
- `ows inspect [package]`: inspect package metadata and findings.
- `ows report [package] [--format text|json]`: write a reviewer report beside the package.

All commands support `--json` for structured output. `ows init` and `ows package` are the normal student path. No session, upload, checkpoint, server, or credential ceremony is required.
