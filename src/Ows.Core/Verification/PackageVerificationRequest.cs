namespace Ows.Core.Verification;

/// <summary>
/// Describes the inputs required to verify an OWS package.
/// </summary>
public sealed record PackageVerificationRequest {
    /// <summary>
    /// Gets the package path to verify.
    /// </summary>
    public string PackagePath { get; init; } = string.Empty;

}
