# CLI Reference

The canonical command reference is [docs/development/CLI.md](docs/development/CLI.md).

The normal student path is `ows init` followed by `ows package`. Reviewer commands
are `ows verify <package>`, `ows inspect <package>`, and `ows report <package>`;
the package argument is optional when running from the package's project directory.
`ows status` and the Agent/remote
commands are diagnostic or optional integration surfaces.

Legacy session, watcher, event, and remote package subcommands remain callable
for pilot compatibility but are hidden from default CLI help.
