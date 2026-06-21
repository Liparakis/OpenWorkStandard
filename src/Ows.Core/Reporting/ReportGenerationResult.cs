namespace Ows.Core.Reporting;

/// <summary>
/// Represents the outcome of generating a report.
/// </summary>
public sealed record ReportGenerationResult {
    /// <summary>
    /// Gets the selected report format.
    /// </summary>
    public ReportFormat Format { get; init; }

    /// <summary>
    /// Gets the rendered report body.
    /// </summary>
    public string Content { get; init; } = string.Empty;
}
