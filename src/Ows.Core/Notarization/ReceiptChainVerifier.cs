using System.Text.Json;

using Ows.Core.Hashing;

namespace Ows.Core.Notarization;

/// <summary>
/// Provides canonical receipt issuance and verification helpers for the in-memory notarization flow.
/// </summary>
public static class ReceiptChainVerifier
{
    /// <summary>
    /// Gets the expected previous-hash value for the first receipt in a chain.
    /// </summary>
    public const string GenesisPreviousReceiptHash = "";

    /// <summary>
    /// Issues a chained receipt for the supplied checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to receipt.</param>
    /// <param name="previousReceiptHash">The previous receipt hash, or the genesis value for the first receipt.</param>
    /// <returns>A newly issued chained receipt.</returns>
    public static CheckpointReceipt IssueReceipt(Checkpoint checkpoint, string previousReceiptHash)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var receiptWithoutHash = new CheckpointReceipt
        {
            SessionId = checkpoint.SessionId,
            SequenceNumber = checkpoint.SequenceNumber,
            TimelineHeadHash = checkpoint.TimelineHeadHash,
            PreviousReceiptHash = previousReceiptHash
        };

        return receiptWithoutHash with { ReceiptHash = ComputeReceiptHash(receiptWithoutHash) };
    }

    /// <summary>
    /// Computes the canonical receipt hash excluding the <see cref="CheckpointReceipt.ReceiptHash"/> field itself.
    /// </summary>
    /// <param name="receipt">The receipt to hash.</param>
    /// <returns>The lower-case SHA-256 digest of the canonical receipt JSON.</returns>
    public static string ComputeReceiptHash(CheckpointReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var canonicalReceipt = new
        {
            receipt.SessionId,
            receipt.SequenceNumber,
            receipt.TimelineHeadHash,
            receipt.PreviousReceiptHash,
            receipt.ServerTimestamp
        };

        return new Sha256HashService().ComputeHash(JsonSerializer.Serialize(canonicalReceipt));
    }

    /// <summary>
    /// Verifies that a receipt chain is continuous and internally consistent.
    /// </summary>
    /// <param name="receiptChain">The receipt chain to verify.</param>
    /// <returns><see langword="true"/> when the chain is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(ReceiptChain receiptChain)
    {
        ArgumentNullException.ThrowIfNull(receiptChain);

        var expectedPreviousReceiptHash = GenesisPreviousReceiptHash;
        var expectedSequenceNumber = 1;

        foreach (var receipt in receiptChain.Receipts)
        {
            if (receipt.SessionId != receiptChain.SessionId)
            {
                return false;
            }

            if (receipt.SequenceNumber != expectedSequenceNumber)
            {
                return false;
            }

            if (!string.Equals(receipt.PreviousReceiptHash, expectedPreviousReceiptHash, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(receipt.ReceiptHash, ComputeReceiptHash(receipt), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            expectedPreviousReceiptHash = receipt.ReceiptHash;
            expectedSequenceNumber++;
        }

        return true;
    }
}
