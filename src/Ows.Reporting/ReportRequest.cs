using Ows.Core.Verification;

namespace Ows.Reporting;

/// <summary>
/// Describes the inputs required to generate an OWS report.
/// </summary>
public sealed record ReportRequest
{
    /// <summary>
    /// Gets the verification result to format.
    /// </summary>
    public required VerificationResult VerificationResult { get; init; }

    /// <summary>
    /// Gets the requested output format.
    /// </summary>
    public ReportFormat Format { get; init; }
}
