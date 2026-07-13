namespace Ows.Core.Verification;

/// <summary>
///     Represents the verification trust grade for a package or evidence store.
/// </summary>
public enum TrustStatus {
    /// <summary>
    ///     The package signature and local evidence align with no meaningful integrity concerns.
    /// </summary>
    Verified,

    /// <summary>
    ///     The evidence is mostly trustworthy but includes explainable concerns.
    /// </summary>
    Degraded,

    /// <summary>
    ///     The evidence is locally consistent but lacks enough trust anchors for verification.
    /// </summary>
    Unverified,

    /// <summary>
    ///     The evidence failed structural or integrity validation.
    /// </summary>
    Invalid
}
