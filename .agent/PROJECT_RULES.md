# OWS Project Rules

## Project identity

Open Work Standard (OWS) is a local-first academic work provenance system. Use the term `Open Work Standard` consistently. Prefer `Work Verifier`, `Review Signals`, `Work Version Graph`, `.ows`, and `.owspkg`.

## Naming rules

- Use domain names that describe academic work provenance precisely.
- Avoid vague names such as `Manager`, `Helper`, `Processor`, or `Thing`.
- Do not reintroduce old terminology such as `Open Assessment Protocol`, `.oap`, `.oapkg`, `Professor Scanner`, or `Risk Signals`.

## Privacy boundaries

- OWS may collect file events, project events, build/test/run events, hashes, deltas, manifests, and provenance metadata that stay within the tracked project boundary.
- OWS must never collect raw keystrokes, passwords, private messages, browser content, browser history, webcam data, microphone data, or unrelated personal files.
- New features must preserve the local-first model unless the specification is explicitly expanded.

## Architecture boundaries

- Keep `Ows.Core` platform-independent.
- Keep OS-specific and UI-specific concerns out of `Ows.Core`.
- `Ows.Desktop` remains a placeholder until Avalonia work is explicitly requested.
- Do not fake package creation, verification, or surveillance features. If functionality does not exist, return a clear placeholder or failure message.

## Stack choices

- Target `.NET 9` while it is available in the local environment.
- Use `System.Text.Json` for JSON serialization unless there is a concrete reason not to.
- Prefer built-in .NET and Microsoft extensions before introducing new infrastructure dependencies.
- Keep SQLite usage scoped to the local agent and evidence index concerns.

## Documentation requirements

- Update `README.md` when user-facing setup or status changes.
- Any change that affects OWS capabilities, roadmap, or deferred scope must update `docs/development/ROADMAP_CHECKLIST.md`.
- Keep `SPEC.md` and `ARCHITECTURE.md` aligned with the actual code direction.
- New public-facing terms must be added to `docs/reference/GLOSSARY.md`.
- Privacy or data-collection changes must update `docs/core/PRIVACY.md` and `docs/core/SECURITY.md` in the same change.

## Testing requirements

- Add or update tests for all new public behavior.
- Prefer testing domain behavior in `Ows.Core.Tests` before wiring outer layers.
- Keep placeholder services honest by testing their current explicit messages instead of inventing fake success paths.
- `dotnet build` and `dotnet test` must pass before completion.

## Commit rules

- Use clear, scoped commit messages.
- Do not bundle unrelated refactors into feature commits.
- Preserve user changes that are outside the requested scope.

## Cross-platform expectations

- Assume Windows and macOS are first-class targets.
- Normalize paths and avoid Windows-only APIs in `Ows.Core`.
- Future file watching must support a polling fallback in addition to native watcher signals.

## Container location

- If Docker or Kubernetes infrastructure is introduced, use `D:\Containers\OWS` as the default local container storage/work directory unless the user explicitly says otherwise.

## Initial non-goals

- AI detection
- Browser lockdown
- Webcam or microphone recording
- Keylogging
- Cloud dashboard
- LMS integrations
- Blockchain
- Full IDE plugins
- Automatic misconduct judgment
- Background service installation
- Avalonia desktop UI implementation
