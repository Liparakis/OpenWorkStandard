# CLI Reference

The OWS CLI manages local provenance tracking, remote verification sessions, packaging, and submissions.

---

## Global Options

- `--json`: Outputs command results in structured JSON format (maps to `OwsCliResponse`). Error messages are structured in the `Errors` list. API keys are automatically redacted in the output.

---

## `ows init`

- **Purpose**: Initialize local OWS tracking metadata for a project.
- **Usage**: `ows init [--json]`
- **Behavior**: Creates `.ows/`, `.ows/config.json`, and `.ows/timeline.jsonl`.
- **JSON Output**: Returns the initialization status and project root.

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
  - `InstitutionId` (string?)
  - `AssessmentId` (string?)
  - `StudentUserId` (string?)
  - `CourseOfferingId` (string?)
  - `LastCheckpointAt` (string?)
  - `LastHeartbeatAt` (string?)
  - `WatcherRunning` (bool)

---

## `ows watch`

- **Purpose**: Start/stop the file-system tracking agent.
- **Subcommands**:
  - `start`: Starts the watcher in the background. If `--json`, prints startup status and blocks.
    - Options:
      - `--poll`: Use polling fallback.
      - `--debounce <ms>`: Debounce interval (default: 500).
  - `stop`: Stops the running watcher cleanly via the `.ows/watcher.stop` signal file.

---

## `ows session`

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
- **Subcommands**:
  - `(default)`: Running `ows package` creates a `.owspkg` archive of the project.
  - `upload`: Uploads the `.owspkg` archive to the verifier using a multipart form.
    - Options:
      - `--package-path <path>`: override package path.
      - `--server <url>`: override verifier URL.
  - `status`: Queries package verification status from the verifier.
    - Options:
      - `--package-id <id>`: override package ID.
      - `--server <url>`: override verifier URL.

---

## `ows verify`

- **Purpose**: Verify a package's integrity and cross-check receipts.
- **Usage**: `ows verify [--server <url>]`

---

## `ows report`

- **Purpose**: Generate an integrity report from verification output.
- **Usage**: `ows report [--format text|json]`
