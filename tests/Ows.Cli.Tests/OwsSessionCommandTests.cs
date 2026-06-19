using System.Text.Json;
using System.Text;

using FluentAssertions;

using Ows.Cli;
using Ows.Core;
using Ows.Core.Notarization;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the local session and checkpoint CLI behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsSessionCommandTests
{
    /// <summary>
    /// Verifies that session start creates persisted local session state.
    /// </summary>
    [Fact]
    public async Task SessionStart_ShouldCreateSessionAndReceiptsFiles()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(["session", "start"]).InvokeAsync();

            exitCode.Should().Be(0);
            File.Exists(Path.Combine(projectRoot, ".ows", "session.json")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName)).Should().BeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that checkpoint appends a receipt for the current timeline head.
    /// </summary>
    [Fact]
    public async Task Checkpoint_ShouldAppendReceiptToReceiptsFile()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync();
            await OwsCommandFactory.BuildRootCommand().Parse(["session", "start"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(["session", "checkpoint"]).InvokeAsync();
            var receiptsPath = Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName);
            var receiptChain = JsonSerializer.Deserialize<ReceiptChain>(File.ReadAllText(receiptsPath));

            exitCode.Should().Be(0);
            receiptChain.Should().NotBeNull();
            receiptChain!.Receipts.Should().ContainSingle();
            receiptChain.Receipts[0].TimelineHeadHash.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that session start can target a remote verifier and persist that choice locally.
    /// </summary>
    [Fact]
    public async Task SessionStart_WithServer_ShouldPersistVerifierUrl()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-remote-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        using var verifierServer = new StubVerifierServer((method, path) => path switch
        {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            var value when value == $"sessions/{RemoteSessionId}/checkpoints" && method == "POST" => CreateRemoteReceipt(),
            var value when value == $"sessions/{RemoteSessionId}/receipts" && method == "GET" => new ReceiptChain
            {
                SessionId = RemoteSessionId,
                Receipts = [CreateRemoteReceipt()]
            },
            _ => null
        });

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync();
            var sessionState = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, ".ows", "session.json")));

            exitCode.Should().Be(0);
            sessionState.RootElement.GetProperty("SessionId").GetString().Should().Be(RemoteSessionId.Value);
            sessionState.RootElement.GetProperty("VerifierUrl").GetString().Should().Be(verifierServer.BaseUrl);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that remote checkpointing appends the receipt chain returned by the verifier.
    /// </summary>
    [Fact]
    public async Task Checkpoint_WithServer_ShouldAppendReceiptToReceiptsFile()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-remote-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();
        using var verifierServer = new StubVerifierServer((method, path) => path switch
        {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            var value when value == $"sessions/{RemoteSessionId}/checkpoints" && method == "POST" => CreateRemoteReceipt(),
            var value when value == $"sessions/{RemoteSessionId}/receipts" && method == "GET" => new ReceiptChain
            {
                SessionId = RemoteSessionId,
                Receipts = [CreateRemoteReceipt()]
            },
            _ => null
        });

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync();
            await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(["session", "checkpoint"]).InvokeAsync();
            var receiptChain = JsonSerializer.Deserialize<ReceiptChain>(
                File.ReadAllText(Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName)));

            exitCode.Should().Be(0);
            receiptChain.Should().NotBeNull();
            receiptChain!.SessionId.Should().Be(RemoteSessionId);
            receiptChain.Receipts.Should().ContainSingle();
            receiptChain.Receipts[0].ReceiptHash.Should().Be(RemoteReceiptHash);
            verifierServer.RequestedPaths.Should().ContainInOrder(
                "sessions",
                $"sessions/{RemoteSessionId}/checkpoints",
                $"sessions/{RemoteSessionId}/receipts");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    private static readonly AssessmentSessionId RemoteSessionId = new("remote-session-1");
    private const string RemoteReceiptHash = "remote-receipt-1";

    /// <summary>
    /// Creates the single receipt returned by the stub verifier.
    /// </summary>
    /// <returns>The stub remote receipt.</returns>
    private static CheckpointReceipt CreateRemoteReceipt() =>
        new()
        {
            SessionId = RemoteSessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "timeline-head-1",
            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
            ReceiptHash = RemoteReceiptHash
        };
}
