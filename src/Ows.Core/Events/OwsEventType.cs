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
    PackageCreated
}
