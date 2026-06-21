namespace Ows.Core.Verification;

/// <summary>
/// Defines the package verification contract.
/// </summary>
public interface IPackageVerifier {
    /// <summary>
    /// Verifies an OWS package.
    /// </summary>
    /// <param name="request">The verification request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current verification outcome.</returns>
    Task<VerificationResult> VerifyAsync(PackageVerificationRequest request, CancellationToken cancellationToken);
}
