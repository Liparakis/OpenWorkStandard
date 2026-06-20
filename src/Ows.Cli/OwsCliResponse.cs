using System;
using System.Collections.Generic;

namespace Ows.Cli;

/// <summary>
/// Unified JSON response structure returned by CLI when executing commands with the --json flag.
/// </summary>
public sealed class OwsCliResponse
{
    /// <summary>Gets or sets whether the command succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the status string (e.g. Ready, Watching, Session active, etc.).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the current project root path.</summary>
    public string? ProjectRoot { get; set; }

    /// <summary>Gets or sets the active session identifier.</summary>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets the submission package identifier.</summary>
    public string? PackageId { get; set; }

    /// <summary>Gets or sets the verifier URL.</summary>
    public string? VerifierUrl { get; set; }

    /// <summary>Gets or sets the institution identifier.</summary>
    public string? InstitutionId { get; set; }

    /// <summary>Gets or sets the assessment identifier.</summary>
    public string? AssessmentId { get; set; }

    /// <summary>Gets or sets the student user identifier.</summary>
    public string? StudentUserId { get; set; }

    /// <summary>Gets or sets the course offering identifier.</summary>
    public string? CourseOfferingId { get; set; }

    /// <summary>Gets or sets the last checkpoint timestamp.</summary>
    public DateTimeOffset? LastCheckpointAt { get; set; }

    /// <summary>Gets or sets the last heartbeat timestamp.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>Gets or sets the trust status for package verification.</summary>
    public string? TrustStatus { get; set; }

    /// <summary>Gets or sets whether the file watcher is currently running.</summary>
    public bool WatcherRunning { get; set; }

    /// <summary>Gets or sets human-readable output messages.</summary>
    public string? Message { get; set; }

    /// <summary>Gets the list of errors encountered during execution.</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>Gets the list of warnings encountered during execution.</summary>
    public List<string> Warnings { get; set; } = [];
}
