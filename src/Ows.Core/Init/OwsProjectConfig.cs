using System;
using System.Text.Json.Serialization;
using Ows.Core.Agent;

namespace Ows.Core.Init;

/// <summary>
/// Represents the project-level configuration stored in .ows/config.json.
/// </summary>
public sealed class OwsProjectConfig
{
    /// <summary>
    /// Gets or sets the OWS version.
    /// </summary>
    [JsonPropertyName("owsVersion")]
    public string OwsVersion { get; set; } = "0.1";

    /// <summary>
    /// Gets or sets the project root path.
    /// </summary>
    [JsonPropertyName("projectRoot")]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Gets or sets when the project was initialized.
    /// </summary>
    [JsonPropertyName("initializedAtUtc")]
    public DateTimeOffset InitializedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the remote verifier URL.
    /// </summary>
    [JsonPropertyName("verifierUrl")]
    public string? VerifierUrl { get; set; }

    /// <summary>
    /// Gets or sets the institution identifier.
    /// </summary>
    [JsonPropertyName("institutionId")]
    public string? InstitutionId { get; set; }

    /// <summary>
    /// Gets or sets the assessment identifier.
    /// </summary>
    [JsonPropertyName("assessmentId")]
    public string? AssessmentId { get; set; }

    /// <summary>
    /// Gets or sets the student user identifier.
    /// </summary>
    [JsonPropertyName("studentUserId")]
    public string? StudentUserId { get; set; }

    /// <summary>
    /// Gets or sets the course offering identifier.
    /// </summary>
    [JsonPropertyName("courseOfferingId")]
    public string? CourseOfferingId { get; set; }

    /// <summary>
    /// Gets or sets whether package upload is enabled.
    /// </summary>
    [JsonPropertyName("uploadEnabled")]
    public bool PackageUploadEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets local file watcher settings.
    /// </summary>
    [JsonPropertyName("watcherSettings")]
    public FileWatcherOptions? WatcherSettings { get; set; }
}
