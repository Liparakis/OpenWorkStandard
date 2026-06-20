namespace Ows.Core.Events;

/// <summary>
/// Identifies the normalized event categories tracked by Open Work Standard.
/// </summary>
public enum OwsEventType
{
    /// <summary>
    /// A file was created inside the tracked project.
    /// </summary>
    FileCreated,

    /// <summary>
    /// A file was modified inside the tracked project.
    /// </summary>
    FileModified,

    /// <summary>
    /// A file was deleted inside the tracked project.
    /// </summary>
    FileDeleted,

    /// <summary>
    /// A tracked project was opened.
    /// </summary>
    ProjectOpened,

    /// <summary>
    /// A tracked project was closed.
    /// </summary>
    ProjectClosed,

    /// <summary>
    /// A build was started.
    /// </summary>
    BuildStarted,

    /// <summary>
    /// A build completed successfully.
    /// </summary>
    BuildSucceeded,

    /// <summary>
    /// A build failed.
    /// </summary>
    BuildFailed,

    /// <summary>
    /// A program was executed.
    /// </summary>
    ProgramExecuted,

    /// <summary>
    /// A test run was executed.
    /// </summary>
    TestExecuted,

    /// <summary>
    /// A large insertion was detected in the tracked work.
    /// </summary>
    LargeInsert,

    /// <summary>
    /// An OWS package was created.
    /// </summary>
    PackageCreated,

    /// <summary>
    /// The OWS file watcher process has started.
    /// </summary>
    WatcherStarted,

    /// <summary>
    /// The OWS file watcher process was stopped cleanly.
    /// </summary>
    WatcherStopped,

    /// <summary>
    /// The OWS file watcher process was interrupted or exited abnormally.
    /// </summary>
    WatcherInterrupted,

    /// <summary>
    /// The OWS file watcher process was recovered after an abnormal exit.
    /// </summary>
    WatcherRecovered,

    /// <summary>
    /// An observation gap was detected during which the watcher was not observing.
    /// </summary>
    ObservationGapDetected,

    /// <summary>
    /// A file change was detected during an unobserved gap.
    /// </summary>
    UnobservedChangeDetected,

    /// <summary>
    /// A large file change was detected during an unobserved gap.
    /// </summary>
    LargeUnobservedChangeDetected

    ,

    /// <summary>
    /// The observed recovery snapshot state was committed to the timeline.
    /// </summary>
    SnapshotUpdated
}
