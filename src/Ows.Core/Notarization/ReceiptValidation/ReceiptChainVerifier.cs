using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ows.Core.Hashing;

namespace Ows.Core.Notarization;

/// <summary>
/// Provides canonical receipt issuance and verification helpers for the in-memory notarization flow.
/// </summary>
public static class ReceiptChainVerifier {
    /// <summary>
    /// Gets the expected previous-hash value for the first receipt in a chain.
    /// </summary>
    public const string GenesisPreviousReceiptHash = "";

    /// <summary>
    /// Issues a chained receipt for the supplied checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to receipt.</param>
    /// <param name="previousReceiptHash">The previous receipt hash, or the genesis value for the first receipt.</param>
    /// <param name="signingKey">The optional server signing key used to sign the receipt hash.</param>
    /// <returns>A newly issued chained receipt.</returns>
    public static CheckpointReceipt IssueReceipt(Checkpoint checkpoint, string previousReceiptHash, string? signingKey = null) {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var issuedAtUtc = NormalizeIssuedAtUtc(DateTimeOffset.UtcNow);
        var receiptWithoutHash = new CheckpointReceipt {
            SessionId = checkpoint.SessionId,
            SequenceNumber = checkpoint.SequenceNumber,
            TimelineHeadHash = checkpoint.TimelineHeadHash,
            PreviousReceiptHash = previousReceiptHash,
            ServerTimestamp = new ServerTimestamp {
                IssuedAtUtc = issuedAtUtc
            },
            SigningKeyFingerprint = ComputeFingerprint(signingKey)
        };

        var receiptHash = ComputeReceiptHash(receiptWithoutHash);
        return receiptWithoutHash with {
            ReceiptHash = receiptHash,
            ServerSignature = ComputeServerSignature(receiptHash, signingKey)
        };
    }

    private static DateTimeOffset NormalizeIssuedAtUtc(DateTimeOffset value) {
        const long ticksPerMicrosecond = 10;
        var utcValue = value.ToUniversalTime();
        var normalizedTicks = utcValue.Ticks - (utcValue.Ticks % ticksPerMicrosecond);
        return new DateTimeOffset(normalizedTicks, TimeSpan.Zero);
    }

    /// <summary>
    /// Computes the canonical receipt hash excluding the <see cref="CheckpointReceipt.ReceiptHash"/> field itself.
    /// </summary>
    /// <param name="receipt">The receipt to hash.</param>
    /// <returns>The lower-case SHA-256 digest of the canonical receipt JSON.</returns>
    private static string ComputeReceiptHash(CheckpointReceipt receipt) {
        ArgumentNullException.ThrowIfNull(receipt);

        var canonicalReceipt = new {
            receipt.SessionId,
            receipt.SequenceNumber,
            receipt.TimelineHeadHash,
            receipt.PreviousReceiptHash,
            receipt.ServerTimestamp,
            receipt.SigningKeyFingerprint
        };

        return new Sha256HashService().ComputeHash(JsonSerializer.Serialize(canonicalReceipt));
    }

    /// <summary>
    /// Computes the optional HMAC server signature for a receipt hash.
    /// </summary>
    /// <param name="receiptHash">The committed receipt hash to sign.</param>
    /// <param name="signingKey">The server signing key, or empty for unsigned local receipts.</param>
    /// <returns>The lower-case HMAC-SHA256 signature, or an empty string when no signing key is configured.</returns>
    private static string ComputeServerSignature(string receiptHash, string? signingKey) {
        if (string.IsNullOrWhiteSpace(signingKey)) {
            return string.Empty;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(receiptHash))).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint of the server signing key.
    /// </summary>
    /// <param name="signingKey">The server signing key.</param>
    /// <returns>A lower-case hex-encoded SHA-256 fingerprint of the key, or an empty string if null/empty.</returns>
    public static string ComputeFingerprint(string? signingKey) {
        if (string.IsNullOrWhiteSpace(signingKey)) {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(signingKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that a receipt chain is continuous and internally consistent.
    /// </summary>
    /// <param name="receiptChain">The receipt chain to verify.</param>
    /// <returns><see langword="true"/> when the chain is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(ReceiptChain receiptChain) => IsValid(receiptChain, signingKey: null);

    /// <summary>
    /// Verifies that a receipt chain is continuous, internally consistent, and signed when a key is provided.
    /// </summary>
    /// <param name="receiptChain">The receipt chain to verify.</param>
    /// <param name="signingKey">The optional server signing key used to verify receipt signatures.</param>
    /// <returns><see langword="true"/> when the chain is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(ReceiptChain receiptChain, string? signingKey) {
        ArgumentNullException.ThrowIfNull(receiptChain);

        var expectedPreviousReceiptHash = GenesisPreviousReceiptHash;
        var expectedSequenceNumber = 1;

        foreach (var receipt in receiptChain.Receipts) {
            if (receipt.SessionId != receiptChain.SessionId) {
                return false;
            }

            if (receipt.SequenceNumber != expectedSequenceNumber) {
                return false;
            }

            if (!string.Equals(receipt.PreviousReceiptHash, expectedPreviousReceiptHash, StringComparison.Ordinal)) {
                return false;
            }

            if (!string.Equals(receipt.ReceiptHash, ComputeReceiptHash(receipt), StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(signingKey) &&
                !string.Equals(
                    receipt.ServerSignature,
                    ComputeServerSignature(receipt.ReceiptHash, signingKey),
                    StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            expectedPreviousReceiptHash = receipt.ReceiptHash;
            expectedSequenceNumber++;
        }

        return true;
    }
}
