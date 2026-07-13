# CLI Reference

The OWS CLI manages local provenance tracking, optional remote verification sessions, packaging, and submissions.

---

## Global Options

- `--json`: Outputs command results in structured JSON format (maps to `OwsCliResponse`). Error messages are structured in the `Errors` list. API keys are automatically redacted in the output.

---

## `ows init`

- **Purpose**: Initialize local OWS tracking metadata for a project.
- **Usage**: `ows init [--json]`
- **Behavior**: Creates `.ows/`, `.ows/config.json`, `.ows/timeline.jsonl`, and a starter `.owsignore` when one does not already exist, then registers the absolute root with the local Agent.
- **Agent registration**: Registers the absolute project root in the platform Agent registry; registration is idempotent.
- **Availability**: Reports `AgentUnavailable` with an actionable start/install message if the local Agent cannot be reached; initialized metadata and registration are retained so retrying is safe.
- **JSON Output**: Returns the initialization status and project root.

`.owsignore` supports blank lines, `#` comments, directory patterns ending in `/`,
relative paths, `*` and `?` wildcards, and Windows/Unix separator normalization.
Directory names such as `bin/` match at any depth; patterns containing a
non-trailing slash match from the project root. Negation and full `.gitignore`
semantics are not supported. The default file excludes `.ows/`, `.git/`, common build and
dependency directories, logs, environment files, secrets, and common binary
outputs.

## `ows agent run`

- **Purpose**: Run the local OWS Agent host for all registered initialized projects.
- **Usage**: `ows agent run [--poll]`
- **Behavior**: Reuses the existing watcher/recovery pipeline for each registered project and ignores deleted or uninitialized roots. Press `Ctrl+C` to stop the host.
- **Registry override**: Set `OWS_AGENT_REGISTRY_PATH` for a test or controlled deployment; Windows otherwise uses `%ProgramData%\OpenWorkStandard\projects.json` and Unix uses user-local application data.

## Windows setup and service

- **Purpose**: Install the silent Windows Agent service through the Service Control Manager.
- **Build**: `scripts/windows/build-ows-setup.ps1`
- **Install**: Double-click `artifacts/ows-setup/Ows.Setup.exe`; UAC Administrator approval is required, but no service-account password is requested.
- **Uninstall**: Use `Open Work Standard` in Windows Installed apps, or run `artifacts/ows-setup/Ows.Setup.exe --uninstall`; the prompt chooses whether to remove the shared registry. `--purge-data` skips that prompt.
- **Installed apps**: The installer registers `Open Work Standard` in Windows Settings and Control Panel Programs and Features.
- **Boundary**: The Windows-only setup executable owns SCM installation and service hosting; filesystem observation and registry behavior remain in `Ows.Core`.

## Windows Agent bootstrap

- **Status**: Use Services.msc or `sc.exe query OwsAgent`.
- **Behavior**: `OWS Agent` runs as a silent LocalSystem SCM service, watches only explicitly initialized projects, and SCM restarts it after an unexpected exit with 5/30/60-second delays.

---

## `ows status`

- **Purpose**: Show current OWS tracking and session status.
- **Usage**: `ows status [--json]`
- **JSON Output**: Returns `OwsCliResponse` containing:
  - `Success` (bool)
  - `Status` (string)
  - `ProjectRoot` (string)
  - `SessionId` (string?)
  - `VerifierUrl` (string?)
  - `InstitutionId` (string?, opaque external context)
  - `AssessmentId` (string?, opaque external context)
  - `StudentUserId` (string?, opaque external context)
  - `CourseOfferingId` (string?, opaque external context)
  - `LastCheckpointAt` (string?)
  - `LastHeartbeatAt` (string?)
  - `WatcherRunning` (bool)

---

## `ows watch`

This diagnostic command group is hidden from default help. The installed Agent
is the intended background lifecycle.

- **Purpose**: Start/stop the file-system tracking agent.
- **Subcommands**:
  - `start`: Starts the watcher in the background. If `--json`, prints startup status and blocks.
    - Options:
      - `--poll`: Use polling fallback.
      - `--debounce <ms>`: Debounce interval (default: 500).
  - `stop`: Stops the running watcher cleanly via the `.ows/watcher.stop` signal file.

---

## `ows session`

This compatibility command group is hidden from default help and retained for
remote verifier pilot workflows. It is not part of the student routine.

- **Purpose**: Manage remote/local assessment sessions.
- **Subcommands**:
  - `start`: Starts a session and persists details to `.ows/session.json`.
    - Options:
      - `--server <url>`: remote verifier URL.
  - `status`: Displays active session metadata.
  - `heartbeat`: Sends an active heartbeat signal.
  - `checkpoint`: Submits a checkpoint and gets a receipt.

---

## `ows package`

- **Purpose**: Create, upload, or query submission packages.
- **Creation**: `ows package [--sign]` creates a `.owspkg` archive. `--sign` creates or reuses the user-local RSA signing key and emits `signature.json`; without it, the package remains structurally valid and explicitly unsigned.
- **Subcommands**:
  - `(default)`: Running `ows package` creates a `.owspkg` archive of the project.
  - `upload`: Hidden compatibility subcommand; uploads the `.owspkg` archive to the verifier using a multipart form.
    - Options:
      - `--package-path <path>`: override package path.
      - `--server <url>`: override verifier URL.
  - `status`: Hidden compatibility subcommand; queries package verification status from the verifier.
    - Options:
      - `--package-id <id>`: override package ID.
      - `--server <url>`: override verifier URL.

---

## `ows verify`

- **Purpose**: Verify a package's integrity and cross-check receipts.
- **Usage**: `ows verify [<package.owspkg>] [--server <url>]`

---

## `ows inspect`

- **Purpose**: Inspect a local package for reviewers without contacting a verifier.
- **Usage**: `ows inspect [<package.owspkg>] [--package-path <path>] [--json]`
- **Output**: Trust/signature status, canonical package root, artifact count, timeline summary, findings, and integrity errors.

## `ows report`

- **Purpose**: Generate an integrity report from verification output.
- **Usage**: `ows report [<package.owspkg>] [--format text|json]`
