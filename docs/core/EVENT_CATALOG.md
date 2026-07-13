# OWS Event Catalog

Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

All active events are appended to `.ows/timeline.jsonl` and chained by `OwsEventChain`. The local Agent emits filesystem and lifecycle events; build/test/run metadata may be supplied by future adapters without changing the core boundary.

| Event type | Meaning |
| --- | --- |
| `FileCreated` | A project file appeared. |
| `FileModified` | A project file changed. |
| `FileDeleted` | A project file was removed. |
| `ProjectOpened` / `ProjectClosed` | Local project lifecycle transition. |
| `WatcherStarted` / `WatcherStopped` | Agent observation lifecycle. |
| `WatcherInterrupted` / `WatcherRecovered` | Agent interruption and recovery. |
| `ObservationGapDetected` | The Agent recorded an interval it did not observe. |
| `UnobservedChangeDetected` | A change was found during an observation gap. |
| `LargeUnobservedChangeDetected` | A large change was found during an observation gap. |
| `SnapshotUpdated` | Recovery snapshot state was committed to the chain. |
| `BuildStarted` / `BuildSucceeded` / `BuildFailed` | Build metadata, when emitted by an adapter. |
| `TestExecuted` / `ProgramExecuted` | Test or run metadata, when emitted by an adapter. |
| `PackageCreated` | A package was created locally. |

`LargeInsert` is reserved and is not emitted by the current product. Observation findings are neutral review signals, not automated misconduct judgments.
