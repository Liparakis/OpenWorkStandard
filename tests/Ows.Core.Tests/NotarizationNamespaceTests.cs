using System.Text.Json;
using System.Net;
using System.Net.Http;
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

    /// <summary>
    /// Verifies that the in-memory service issues receipts into a valid chain.
    /// </summary>
    [Fact]
    public void SubmitCheckpoint_ShouldAppendReceiptToSessionChain()
    {
        var service = new InMemoryReceiptService();
        var sessionId = service.StartSession();

        var firstReceipt = service.SubmitCheckpoint(new Checkpoint
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "head-1"
        });
        var secondReceipt = service.SubmitCheckpoint(new Checkpoint
        {
            SessionId = sessionId,
            SequenceNumber = 2,
            TimelineHeadHash = "head-2"
        });
        var chain = service.GetReceiptChain(sessionId);

        firstReceipt.SequenceNumber.Should().Be(1);
        secondReceipt.PreviousReceiptHash.Should().Be(firstReceipt.ReceiptHash);
        chain.Receipts.Should().HaveCount(2);
        ReceiptChainVerifier.IsValid(chain).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that checkpoints for unknown sessions are rejected.
    /// </summary>
    [Fact]
    public void SubmitCheckpoint_ShouldRejectUnknownSession()
    {
        var service = new InMemoryReceiptService();

        var act = () => service.SubmitCheckpoint(new Checkpoint
        {
            SessionId = AssessmentSessionId.Create(),
            SequenceNumber = 1,
            TimelineHeadHash = "head-1"
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Unknown assessment session*");
    }

    /// <summary>
    /// Verifies that out-of-order checkpoint sequences are rejected.
    /// </summary>
    [Fact]
    public void SubmitCheckpoint_ShouldRejectOutOfOrderSequence()
    {
        var service = new InMemoryReceiptService();
        var sessionId = service.StartSession();

        var act = () => service.SubmitCheckpoint(new Checkpoint
        {
            SessionId = sessionId,
            SequenceNumber = 2,
            TimelineHeadHash = "head-2"
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Checkpoint sequence 2 is invalid*Expected 1.");
    }

    /// <summary>
    /// Verifies that sessions can be restored from persisted receipts and continued.
    /// </summary>
    [Fact]
    public void RestoreSession_ShouldAllowContinuingReceiptChain()
    {
        var sessionId = AssessmentSessionId.Create();
        var firstReceipt = ReceiptChainVerifier.IssueReceipt(
            new Checkpoint
            {
                SessionId = sessionId,
                SequenceNumber = 1,
                TimelineHeadHash = "head-1"
            },
            ReceiptChainVerifier.GenesisPreviousReceiptHash);
        var service = new InMemoryReceiptService();

        service.RestoreSession(sessionId, [firstReceipt]);
        var secondReceipt = service.SubmitCheckpoint(new Checkpoint
        {
            SessionId = sessionId,
            SequenceNumber = 2,
            TimelineHeadHash = "head-2"
        });
        var chain = service.GetReceiptChain(sessionId);

        secondReceipt.PreviousReceiptHash.Should().Be(firstReceipt.ReceiptHash);
        chain.Receipts.Should().HaveCount(2);
        ReceiptChainVerifier.IsValid(chain).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that invalid persisted receipt chains are rejected on restore.
    /// </summary>
    [Fact]
    public void RestoreSession_ShouldRejectInvalidReceiptChain()
    {
        var sessionId = AssessmentSessionId.Create();
        var invalidReceipt = new CheckpointReceipt
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "head-1",
            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
            ReceiptHash = "not-a-real-hash"
        };
        var service = new InMemoryReceiptService();

        var act = () => service.RestoreSession(sessionId, [invalidReceipt]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot restore invalid receipt chain*");
    }

    /// <summary>
    /// Verifies that the in-memory remote transport can start a session and issue receipts.
    /// </summary>
    [Fact]
    public async Task InMemoryRemoteReceiptTransport_ShouldIssueReceiptsForActiveSession()
    {
        var transport = new InMemoryRemoteReceiptTransport(
            new InMemoryReceiptService(),
            () => new Checkpoint { TimelineHeadHash = "head-1" });

        var sessionId = await transport.StartSessionAsync(CancellationToken.None);
        var receipt = await transport.SendCheckpointAsync(CancellationToken.None);
        var chain = await transport.GetReceiptsAsync(CancellationToken.None);

        sessionId.Value.Should().NotBeNullOrWhiteSpace();
        receipt.SequenceNumber.Should().Be(1);
        chain.SessionId.Should().Be(sessionId);
        chain.Receipts.Should().ContainSingle();
        ReceiptChainVerifier.IsValid(chain).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the in-memory remote transport rejects checkpoint submission before session start.
    /// </summary>
    [Fact]
    public async Task InMemoryRemoteReceiptTransport_ShouldRequireActiveSession()
    {
        var transport = new InMemoryRemoteReceiptTransport(
            new InMemoryReceiptService(),
            () => new Checkpoint { TimelineHeadHash = "head-1" });

        var act = async () => await transport.SendCheckpointAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active assessment session*");
    }

    /// <summary>
    /// Verifies that the HTTPS transport follows the planned session/checkpoint/receipt endpoints.
    /// </summary>
    [Fact]
    public async Task HttpsReceiptTransport_ShouldUsePlannedApiContract()
    {
        var sessionId = AssessmentSessionId.Create();
        var issuedReceipt = new CheckpointReceipt
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "head-1",
            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
            ReceiptHash = "receipt-1"
        };
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');

            if (request.Method == HttpMethod.Post && path == "sessions")
            {
                return JsonResponse(new StartSessionResponse { SessionId = sessionId.Value });
            }

            if (request.Method == HttpMethod.Post && path == $"sessions/{sessionId}/checkpoints")
            {
                return JsonResponse(issuedReceipt);
            }

            if (request.Method == HttpMethod.Get && path == $"sessions/{sessionId}/receipts")
            {
                return JsonResponse(new ReceiptChain
                {
                    SessionId = sessionId,
                    Receipts = [issuedReceipt]
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://ows.test/") };
        var transport = new HttpsReceiptTransport(
            httpClient,
            (activeSessionId, sequenceNumber) => new Checkpoint
            {
                SessionId = activeSessionId,
                SequenceNumber = sequenceNumber,
                TimelineHeadHash = "head-1"
            });

        var startedSessionId = await transport.StartSessionAsync(CancellationToken.None);
        var receipt = await transport.SendCheckpointAsync(CancellationToken.None);
        var receiptChain = await transport.GetReceiptsAsync(CancellationToken.None);

        startedSessionId.Should().Be(sessionId);
        receipt.SequenceNumber.Should().Be(1);
        receiptChain.Receipts.Should().ContainSingle();
        handler.RequestedPaths.Should().ContainInOrder(
            "sessions",
            $"sessions/{sessionId}/checkpoints",
            $"sessions/{sessionId}/receipts");
    }

    /// <summary>
    /// Verifies that the HTTPS transport requires a started session before checkpoints can be sent.
    /// </summary>
    [Fact]
    public async Task HttpsReceiptTransport_ShouldRequireActiveSession()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://ows.test/")
        };
        var transport = new HttpsReceiptTransport(
            httpClient,
            (activeSessionId, sequenceNumber) => new Checkpoint
            {
                SessionId = activeSessionId,
                SequenceNumber = sequenceNumber,
                TimelineHeadHash = "head-1"
            });

        var act = async () => await transport.SendCheckpointAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active assessment session*");
    }

    /// <summary>
    /// Builds a JSON HTTP response for transport contract tests.
    /// </summary>
    /// <param name="value">The response payload.</param>
    /// <returns>The HTTP response containing the JSON payload.</returns>
    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value))
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        /// <summary>
        /// Handles HTTP requests for transport contract tests.
        /// </summary>
        /// <param name="request">The outgoing HTTP request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The stubbed HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri!.PathAndQuery.TrimStart('/'));
            return Task.FromResult(responder(request));
        }
    }
}
