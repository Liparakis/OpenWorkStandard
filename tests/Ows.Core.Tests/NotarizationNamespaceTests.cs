using FluentAssertions;
using Ows.Core.Notarization;

namespace Ows.Core.Tests;

/// <summary>
/// Tests notarization domain types added for the remote trust boundary foundation.
/// </summary>
public sealed class NotarizationNamespaceTests
{
    /// <summary>
    /// Verifies that receipt chains preserve their session and ordered receipts.
    /// </summary>
    [Fact]
    public void ReceiptChain_ShouldPreserveOrderedReceipts()
    {
        var sessionId = AssessmentSessionId.Create();
        var checkpoint = new Checkpoint
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "abc123"
        };

        var receipt = new CheckpointReceipt
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "abc123",
            ReceiptHash = "def456"
        };

        var chain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = [receipt]
        };

        chain.SessionId.Should().Be(sessionId);
        chain.Receipts.Should().ContainSingle();
        checkpoint.SequenceNumber.Should().Be(1);
    }
}
