# OWS Event Catalog

This document defines the Open Work Standard (OWS) event vocabulary, detailing which events are actively emitted by the current implementation and which are reserved for future integrations.

> [!IMPORTANT]
> **Event presence is evidence of recorded activity. Event absence is not proof of misconduct.**
>
> PackageCreated records local packaging after the artifact is written and may appear in the next timeline/package state.

## Summary Table

| Event Type | Status | Emitter | Persisted | Hash Chain | Trust Evidence | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `FileCreated` | **Active** | `ows watch` (file watcher & scan) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a new file is detected or during initial scan. |
| `FileModified` | **Active** | `ows watch` (file watcher) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a file change is saved or debounced. |
| `FileDeleted` | **Active** | `ows watch` (file watcher) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a file is deleted from the tracked directory. |
| `ProjectOpened` | **Active** | `ows watch start` / VS Code extension | Yes (`timeline.jsonl`) | Yes | Yes | Emitted when watcher state transitions from not-running to running. |
| `ProjectClosed` | **Active** | `ows watch stop` / VS Code extension | Yes (`timeline.jsonl`) | Yes | Yes | Emitted when watcher state transitions from running to stopped by user. |
| `BuildStarted` | **Active** | `ows event build-started` CLI / hooks | Yes (`timeline.jsonl`) | Yes | Yes | Emitted explicitly on build initiation. |
| `BuildSucceeded` | **Active** | `ows event build-succeeded` CLI / hooks | Yes (`timeline.jsonl`) | Yes | Yes | Emitted explicitly on build success. |
| `BuildFailed` | **Active** | `ows event build-failed` CLI / hooks | Yes (`timeline.jsonl`) | Yes | Yes | Emitted explicitly on build failure. |
| `ProgramExecuted` | **Active** | `ows event program-executed` CLI / hooks | Yes (`timeline.jsonl`) | Yes | Yes | Emitted explicitly on program execution. |
| `TestExecuted` | **Active** | `ows event test-executed` CLI / hooks | Yes (`timeline.jsonl`) | Yes | Yes | Emitted explicitly on test run. |
| `LargeInsert` | **Reserved** | None | No | No | No | Reserved for future copy-paste detection logic. |
| `PackageCreated` | **Active** | `ows package` | Yes (`timeline.jsonl`) | Yes | Yes | Emitted locally AFTER the package zip is successfully written. |

---

## Event Details

### FileCreated
- **Description**: A file was created inside the tracked project directory.
- **Status**: `Active`
- **Current Emitter**: `ows watch` (during continuous file system watching and the initial scan on startup).
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes (used to establish presence, active work timeline, and file-change consistency).

### FileModified
- **Description**: A file was modified inside the tracked project directory.
- **Status**: `Active`
- **Current Emitter**: `ows watch` (via file system watch change events).
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes (used to establish active work timeline and file-change consistency).

### FileDeleted
- **Description**: A file was deleted from the tracked project directory.
- **Status**: `Active`
- **Current Emitter**: `ows watch` (via file system watch deletion events).
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes (used to track timeline integrity and deletion patterns).

### ProjectOpened
- **Description**: A tracked project was opened or the watcher started.
- **Status**: `Active`
- **Current Emitter**: `ows watch start` (emitted only on transitions from not-running to running).
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes
- **Notes**: Duplicate start attempts do not append duplicate ProjectOpened events.

### ProjectClosed
- **Description**: A tracked project was closed or the watcher stopped.
- **Status**: `Active`
- **Current Emitter**: `ows watch stop` (emitted only on transitions from running to stopped by explicit user action).
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes
- **Notes**: Not emitted for stale PID cleanup, crash detection, failed starts, or stop requests when already stopped.

### BuildStarted
- **Description**: A compiler or build execution was started for the project.
- **Status**: `Active`
- **Current Emitter**: `ows event build-started` CLI command or custom CLI hooks.
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes

### BuildSucceeded
- **Description**: A project build succeeded.
- **Status**: `Active`
- **Current Emitter**: `ows event build-succeeded` CLI command or custom CLI hooks.
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes

### BuildFailed
- **Description**: A project build failed.
- **Status**: `Active`
- **Current Emitter**: `ows event build-failed` CLI command or custom CLI hooks.
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes

### ProgramExecuted
- **Description**: A compiled binary or script from the project was executed.
- **Status**: `Active`
- **Current Emitter**: `ows event program-executed` CLI command or custom CLI hooks.
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes

### TestExecuted
- **Description**: A test suite or unit test was executed.
- **Status**: `Active`
- **Current Emitter**: `ows event test-executed` CLI command or custom CLI hooks.
- **Persistence Status**: Appended to `.ows/timeline.jsonl`.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes

### LargeInsert
- **Description**: A large insertion of text was detected in a single write.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future copy-paste detection or biometrics.

### PackageCreated
- **Description**: An OWS submission package (`.owspkg`) was generated.
- **Status**: `Active`
- **Current Emitter**: `ows package` command.
- **Persistence Status**: Appended locally to `.ows/timeline.jsonl` *after* the package ZIP file has been successfully written.
- **Included in Timeline Hash Chain**: Yes
- **Used in Current Trust Decisions**: Yes
- **Notes**: Written locally so that the created package itself does not include the PackageCreated event for itself. This maintains correct receipt-matching invariants for the generated package while still logging the local packaging event for the next timeline state.
