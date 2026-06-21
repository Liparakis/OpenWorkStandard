namespace Ows.Core.Notarization;

/// <summary>
/// Defines the storage repository for package metadata and verification outcomes.
/// </summary>
public interface IPackageSubmissionStore {
    /// <summary>
    /// Registers or updates a package submission.
    /// </summary>
    /// <param name="request">The package submission request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered package submission response.</returns>
    Task<VerifierPackageSubmissionResponse> SubmitAsync(
        VerifierPackageSubmissionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a registered package submission by its identifier.
    /// </summary>
    /// <param name="submissionId">The package submission identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The submission record, or null if not found.</returns>
    Task<VerifierPackageSubmissionResponse?> GetAsync(
        string submissionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists registered package submissions associated with a verifier session.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The package submissions anchored to the session, ordered by newest first.</returns>
    Task<IReadOnlyList<VerifierPackageSubmissionResponse>> ListBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the verification results for a package submission.
    /// </summary>
    /// <param name="submissionId">The package submission identifier.</param>
    /// <param name="verificationStatus">The status of the verification run (e.g. Registered, Completed).</param>
    /// <param name="trustStatus">The resulting trust grade (e.g. Verified, Unverified, Invalid).</param>
    /// <param name="verificationResultJson">The serialized JSON representation of the verification outcome.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateVerificationResultAsync(
        string submissionId,
        string verificationStatus,
        string trustStatus,
        string verificationResultJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the verification lifecycle state for a package submission.
    /// </summary>
    /// <param name="submissionId">The package submission identifier.</param>
    /// <param name="verificationStatus">The current verification lifecycle state.</param>
    /// <param name="verificationJobId">The optional current verification job identifier.</param>
    /// <param name="trustStatus">The optional trust status.</param>
    /// <param name="verificationResultJson">The optional serialized verification result.</param>
    /// <param name="lastVerificationError">The optional last verification error.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateVerificationStateAsync(
        string submissionId,
        string verificationStatus,
        string? verificationJobId,
        string? trustStatus,
        string? verificationResultJson,
        string? lastVerificationError,
        CancellationToken cancellationToken);
}
