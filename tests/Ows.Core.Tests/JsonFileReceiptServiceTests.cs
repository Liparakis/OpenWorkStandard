using FluentAssertions;
using Ows.Core.Notarization;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the file-backed receipt service used by the verifier server.
/// </summary>
public sealed class JsonFileReceiptServiceTests
{
    /// <summary>
    /// Verifies that sessions survive a service restart and can continue issuing receipts.
    /// </summary>
    [Fact]
    public void StartSession_ShouldPersistAndRestoreReceiptChains()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var firstService = new JsonFileReceiptService(storePath);
            var sessionId = firstService.StartSession();
            var firstReceipt = firstService.SubmitCheckpoint(new Checkpoint
            {
                SessionId = sessionId,
                SequenceNumber = 1,
                TimelineHeadHash = "head-1"
            });

            var secondService = new JsonFileReceiptService(storePath);
            var restoredChain = secondService.GetReceiptChain(sessionId);
            var secondReceipt = secondService.SubmitCheckpoint(new Checkpoint
            {
                SessionId = sessionId,
                SequenceNumber = 2,
                TimelineHeadHash = "head-2"
            });

            restoredChain.Receipts.Should().ContainSingle();
            restoredChain.Receipts[0].ReceiptHash.Should().Be(firstReceipt.ReceiptHash);
            secondReceipt.PreviousReceiptHash.Should().Be(firstReceipt.ReceiptHash);
            ReceiptChainVerifier.IsValid(secondService.GetReceiptChain(sessionId)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Verifies that invalid persisted receipt chains are rejected during startup.
    /// </summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidPersistedReceiptChains()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-invalid-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");
        var sessionId = AssessmentSessionId.Create();
        var invalidChain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts =
            [
                new CheckpointReceipt
                {
                    SessionId = sessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1",
                    PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
                    ReceiptHash = "fake"
                }
            ]
        };

        try
        {
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(storePath, System.Text.Json.JsonSerializer.Serialize(new[] { invalidChain }));

            var act = () => new JsonFileReceiptService(storePath);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot restore invalid receipt chain*");
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}
