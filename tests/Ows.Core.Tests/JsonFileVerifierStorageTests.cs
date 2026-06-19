using FluentAssertions;
using Ows.Core.Events;
using Ows.Core.Notarization;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the file-backed verifier storage used by the verifier server.
/// </summary>
public sealed class JsonFileVerifierStorageTests
{
    /// <summary>
    /// Verifies that creating a session persists it across a storage restart.
    /// </summary>
    [Fact]
    public async Task CreateSessionAsync_ShouldPersistSession()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-session-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var firstStorage = new JsonFileVerifierStorage(storePath);
            var createdSession = await firstStorage.CreateSessionAsync(CancellationToken.None);

            var secondStorage = new JsonFileVerifierStorage(storePath);
            var restoredSession = await secondStorage.GetSessionAsync(createdSession.Id, CancellationToken.None);

            restoredSession.Id.Should().Be(createdSession.Id);
            restoredSession.CheckpointCount.Should().Be(0);
            restoredSession.HeadReceiptHash.Should().Be(ReceiptChainVerifier.GenesisPreviousReceiptHash);
            restoredSession.HeadEventHash.Should().Be(OwsEventChain.GenesisPreviousEventHash);
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
    /// Verifies that sessions survive a storage restart and can continue issuing receipts.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldPersistAndRestoreReceiptChains()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var firstStorage = new JsonFileVerifierStorage(storePath);
            var session = await firstStorage.CreateSessionAsync(CancellationToken.None);
            var firstReceipt = await firstStorage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1"
                },
                CancellationToken.None);

            var secondStorage = new JsonFileVerifierStorage(storePath);
            var restoredChain = await secondStorage.GetReceiptsAsync(session.Id, CancellationToken.None);
            var secondReceipt = await secondStorage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 2,
                    TimelineHeadHash = "head-2"
                },
                CancellationToken.None);

            restoredChain.Receipts.Should().ContainSingle();
            restoredChain.Receipts[0].ReceiptHash.Should().Be(firstReceipt.ReceiptHash);
            secondReceipt.PreviousReceiptHash.Should().Be(firstReceipt.ReceiptHash);
            ReceiptChainVerifier.IsValid(await secondStorage.GetReceiptsAsync(session.Id, CancellationToken.None)).Should().BeTrue();
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
    /// Verifies that multiple appended checkpoints preserve durable order.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldPreserveReceiptOrder()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-order-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);

            await storage.AppendCheckpointAsync(new Checkpoint { SessionId = session.Id, SequenceNumber = 1, TimelineHeadHash = "head-1" }, CancellationToken.None);
            await storage.AppendCheckpointAsync(new Checkpoint { SessionId = session.Id, SequenceNumber = 2, TimelineHeadHash = "head-2" }, CancellationToken.None);
            await storage.AppendCheckpointAsync(new Checkpoint { SessionId = session.Id, SequenceNumber = 3, TimelineHeadHash = "head-3" }, CancellationToken.None);

            var chain = await storage.GetReceiptsAsync(session.Id, CancellationToken.None);

            chain.Receipts.Select(receipt => receipt.SequenceNumber).Should().Equal([1, 2, 3]);
            chain.Receipts.Select(receipt => receipt.TimelineHeadHash).Should().Equal(["head-1", "head-2", "head-3"]);
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
    /// Verifies that the durable session head reflects the latest committed checkpoint state.
    /// </summary>
    [Fact]
    public async Task GetHeadAsync_ShouldReturnLatestDurableState()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-head-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);
            var firstReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1"
                },
                CancellationToken.None);
            var secondReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 2,
                    TimelineHeadHash = "head-2"
                },
                CancellationToken.None);

            var head = await storage.GetHeadAsync(session.Id, CancellationToken.None);

            head.SessionId.Should().Be(session.Id.Value);
            head.LastSequenceNumber.Should().Be(2);
            head.LastTimelineHeadHash.Should().Be("head-2");
            head.LastReceiptHash.Should().Be(secondReceipt.ReceiptHash);
            head.LastReceiptHash.Should().NotBe(firstReceipt.ReceiptHash);
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
    /// Verifies that retrying the same checkpoint sequence with the same payload returns the committed receipt.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldReturnCommittedReceiptForSameSequenceAndPayload()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-idempotent-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);

            var firstReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1"
                },
                CancellationToken.None);
            var retriedReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1"
                },
                CancellationToken.None);

            retriedReceipt.Should().BeEquivalentTo(firstReceipt);
            (await storage.GetReceiptsAsync(session.Id, CancellationToken.None)).Receipts.Should().ContainSingle();
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
    /// Verifies that retrying the same checkpoint sequence with a different payload is rejected.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldRejectSameSequenceWithDifferentPayload()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-idempotent-reject-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);

            _ = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1"
                },
                CancellationToken.None);

            var act = async () => await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-2"
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Checkpoint sequence 1 already exists*different payload.");
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
    /// Verifies that retrying the same idempotency key with the same payload returns the committed receipt.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldReturnCommittedReceiptForSameIdempotencyKeyAndPayload()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-idempotency-key-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);
            var firstReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1",
                    IdempotencyKey = "same-request"
                },
                CancellationToken.None);
            var retriedReceipt = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1",
                    IdempotencyKey = "same-request"
                },
                CancellationToken.None);

            retriedReceipt.Should().BeEquivalentTo(firstReceipt);
            (await storage.GetReceiptsAsync(session.Id, CancellationToken.None)).Receipts.Should().ContainSingle();
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
    /// Verifies that retrying the same idempotency key with a different payload is rejected.
    /// </summary>
    [Fact]
    public async Task AppendCheckpointAsync_ShouldRejectSameIdempotencyKeyWithDifferentPayload()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"ows-verifier-store-idempotency-key-reject-{Guid.NewGuid():N}");
        var storePath = Path.Combine(directoryPath, "receipts.json");

        try
        {
            var storage = new JsonFileVerifierStorage(storePath);
            var session = await storage.CreateSessionAsync(CancellationToken.None);

            _ = await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 1,
                    TimelineHeadHash = "head-1",
                    IdempotencyKey = "same-request"
                },
                CancellationToken.None);

            var act = async () => await storage.AppendCheckpointAsync(
                new Checkpoint
                {
                    SessionId = session.Id,
                    SequenceNumber = 2,
                    TimelineHeadHash = "head-2",
                    IdempotencyKey = "same-request"
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Idempotency key same-request already exists*different payload.");
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
        var invalidSnapshot = new
        {
            Sessions = new[]
            {
                new VerifierSessionRecord
                {
                    Id = sessionId,
                    CreatedAtUtc = DateTimeOffset.Parse("2026-06-19T00:00:00+00:00", null),
                    HeadReceiptHash = "fake",
                    HeadEventHash = "head-1",
                    CheckpointCount = 1
                }
            },
            ReceiptChains = new[]
            {
                new ReceiptChain
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
                            ReceiptHash = "fake",
                            ServerTimestamp = new ServerTimestamp
                            {
                                IssuedAtUtc = DateTimeOffset.Parse("2026-06-19T00:00:00+00:00", null)
                            }
                        }
                    ]
                }
            }
        };

        try
        {
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(storePath, System.Text.Json.JsonSerializer.Serialize(invalidSnapshot));

            var act = () => new JsonFileVerifierStorage(storePath);

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
