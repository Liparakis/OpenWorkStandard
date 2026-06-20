# OWS Event Catalog

This document defines the Open Work Standard (OWS) event vocabulary, detailing which events are actively emitted by the current implementation and which are reserved for future integrations.

## Summary Table

| Event Type | Status | Emitter | Persisted | Hash Chain | Trust Evidence | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `FileCreated` | **Active** | `ows watch` (file watcher & scan) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a new file is detected or during initial scan. |
| `FileModified` | **Active** | `ows watch` (file watcher) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a file change is saved or debounced. |
| `FileDeleted` | **Active** | `ows watch` (file watcher) | Yes (`timeline.jsonl`) | Yes | Yes (Fidelity) | Emitted when a file is deleted from the tracked directory. |
| `ProjectOpened` | **Reserved** | None | No | No | No | Reserved for future IDE plugin integrations. |
| `ProjectClosed` | **Reserved** | None | No | No | No | Reserved for future IDE plugin integrations. |
| `BuildStarted` | **Reserved** | None | No | No | No | Reserved for future compiler/build tool integration. |
| `BuildSucceeded` | **Reserved** | None | No | No | No | Reserved for future compiler/build tool integration. |
| `BuildFailed` | **Reserved** | None | No | No | No | Reserved for future compiler/build tool integration. |
| `ProgramExecuted` | **Reserved** | None | No | No | No | Reserved for future run/debugger telemetry. |
| `TestExecuted` | **Reserved** | None | No | No | No | Reserved for future test runner integration. |
| `LargeInsert` | **Reserved** | None | No | No | No | Reserved for future copy-paste detection logic. |
| `PackageCreated` | **Reserved** | None | No | No | No | Reserved to maintain receipt-matching validation invariants. |

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
- **Description**: A tracked project was opened in an IDE or editor.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Part of the protocol schema for future IDE plugin integrations.

### ProjectClosed
- **Description**: A tracked project was closed in an IDE or editor.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Part of the protocol schema for future IDE plugin integrations.

### BuildStarted
- **Description**: A compiler or build execution was started for the project.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future toolchain integration.

### BuildSucceeded
- **Description**: A project build succeeded.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future toolchain integration.

### BuildFailed
- **Description**: A project build failed.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future toolchain integration.

### ProgramExecuted
- **Description**: A compiled binary or script from the project was executed.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future debugger/execution telemetry.

### TestExecuted
- **Description**: A test suite or unit test was executed.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future test runner integration.

### LargeInsert
- **Description**: A large insertion of text (e.g., potential copy-paste) was detected in a single write.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Reserved for future copy-paste detection or biometrics.

### PackageCreated
- **Description**: An OWS submission package (`.owspkg`) was generated.
- **Status**: `Reserved`
- **Current Emitter**: None
- **Persistence Status**: None
- **Included in Timeline Hash Chain**: No
- **Used in Current Trust Decisions**: No
- **Notes**: Kept as `Reserved` to preserve the core verification invariant that the local timeline head hash must exactly match the notarized receipt chain head hash. Since a package's own creation event cannot be notarized within the package before it is created, this event is not appended to the packaged timeline.
