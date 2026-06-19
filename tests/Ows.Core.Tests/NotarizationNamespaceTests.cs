using System.Text.Json;
using FluentAssertions;
using Ows.Core.Events;
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
            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
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

    /// <summary>
    /// Verifies that issued receipts form a valid chain.
    /// </summary>
    [Fact]
    public void IssueReceipt_ShouldProduceValidReceiptChain()
    {
        var sessionId = AssessmentSessionId.Create();
        var firstCheckpoint = new Checkpoint { SessionId = sessionId, SequenceNumber = 1, TimelineHeadHash = "head-1" };
        var firstReceipt = ReceiptChainVerifier.IssueReceipt(firstCheckpoint, ReceiptChainVerifier.GenesisPreviousReceiptHash);
        var secondCheckpoint = new Checkpoint { SessionId = sessionId, SequenceNumber = 2, TimelineHeadHash = "head-2" };
        var secondReceipt = ReceiptChainVerifier.IssueReceipt(secondCheckpoint, firstReceipt.ReceiptHash);
        var chain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = [firstReceipt, secondReceipt]
        };

        ReceiptChainVerifier.IsValid(chain).Should().BeTrue();
        secondReceipt.PreviousReceiptHash.Should().Be(firstReceipt.ReceiptHash);
    }

    /// <summary>
    /// Verifies that tampered receipts fail chain validation.
    /// </summary>
    [Fact]
    public void IsValid_ShouldFailWhenReceiptChainIsTampered()
    {
        var sessionId = AssessmentSessionId.Create();
        var firstReceipt = ReceiptChainVerifier.IssueReceipt(
            new Checkpoint { SessionId = sessionId, SequenceNumber = 1, TimelineHeadHash = "head-1" },
            ReceiptChainVerifier.GenesisPreviousReceiptHash);
        var secondReceipt = ReceiptChainVerifier.IssueReceipt(
            new Checkpoint { SessionId = sessionId, SequenceNumber = 2, TimelineHeadHash = "head-2" },
            firstReceipt.ReceiptHash) with { TimelineHeadHash = "tampered-head" };
        var chain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = [firstReceipt, secondReceipt]
        };

        ReceiptChainVerifier.IsValid(chain).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that checkpoints can be derived from the current timeline head.
    /// </summary>
    [Fact]
    public void FromTimeline_ShouldUseLastChainedEventHash()
    {
        var timelinePath = Path.Combine(Path.GetTempPath(), $"ows-checkpoint-{Guid.NewGuid():N}.jsonl");
        var firstEvent = OwsEventChain.CreateChainedEvent(
            new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", RelativePath = "a.txt" },
            OwsEventChain.GenesisPreviousEventHash);
        var secondEvent = OwsEventChain.CreateChainedEvent(
            new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "a.txt" },
            firstEvent.EventHash);

        try
        {
            File.WriteAllText(timelinePath, $"{JsonSerializer.Serialize(firstEvent)}{Environment.NewLine}{JsonSerializer.Serialize(secondEvent)}");

            var checkpoint = Checkpoint.FromTimeline(timelinePath, AssessmentSessionId.Create(), 2);

            checkpoint.SequenceNumber.Should().Be(2);
            checkpoint.TimelineHeadHash.Should().Be(secondEvent.EventHash);
        }
        finally
        {
            if (File.Exists(timelinePath))
            {
                File.Delete(timelinePath);
            }
        }
    }

    /// <summary>
    /// Verifies that empty timelines produce the genesis head hash.
    /// </summary>
    [Fact]
    public void FromTimeline_ShouldUseGenesisHashWhenTimelineIsEmpty()
    {
        var timelinePath = Path.Combine(Path.GetTempPath(), $"ows-checkpoint-empty-{Guid.NewGuid():N}.jsonl");

        try
        {
            File.WriteAllText(timelinePath, string.Empty);

            var checkpoint = Checkpoint.FromTimeline(timelinePath, AssessmentSessionId.Create(), 1);

            checkpoint.TimelineHeadHash.Should().Be(OwsEventChain.GenesisPreviousEventHash);
        }
        finally
        {
            if (File.Exists(timelinePath))
            {
                File.Delete(timelinePath);
            }
        }
    }
}
