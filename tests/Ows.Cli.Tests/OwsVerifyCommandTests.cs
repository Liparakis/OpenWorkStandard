using FluentAssertions;
using Ows.Cli;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Notarization;
using System.IO.Compression;
using System.Text.Json;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the verify command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsVerifyCommandTests
{
    /// <summary>
    /// Verifies that the verify command succeeds for a package created by the CLI flow.
    /// </summary>
    [Fact]
    public async Task VerifyCommand_ShouldSucceedForCreatedPackage()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var verifyResult = await OwsCommandFactory.BuildRootCommand().Parse(["verify"]).InvokeAsync();

            verifyResult.Should().Be(0);
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
    /// Verifies that live verifier cross-check succeeds for an untampered packaged receipt chain.
    /// </summary>
    [Fact]
    public async Task VerifyCommand_WithServer_ShouldSucceedWhenRemoteReceiptsMatchPackage()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-verify-remote-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();
        var sessionId = new AssessmentSessionId("verify-session-1");
        var remoteReceiptChain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = [CreateReceipt(sessionId, "stub-head")]
        };
        using var verifierServer = new StubVerifierServer((method, path) => path switch
        {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = sessionId.Value },
            var value when value == $"sessions/{sessionId}/checkpoints" && method == "POST" => CreateReceipt(sessionId, "stub-head"),
            var value when value == $"sessions/{sessionId}/receipts" && method == "GET" => remoteReceiptChain,
            _ => null
        });

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["session", "start", "--server", verifierServer.BaseUrl]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["session", "checkpoint"]).InvokeAsync()).Should().Be(0);
            var actualTimelineHeadHash = OwsEventChain.ReadLastEventHash(Path.Combine(projectRoot, ".ows", OwsConstants.TimelineFileName));
            remoteReceiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [CreateReceipt(sessionId, actualTimelineHeadHash)]
            };
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName),
                JsonSerializer.Serialize(remoteReceiptChain));
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var verifyResult = await OwsCommandFactory.BuildRootCommand().Parse(["verify", "--server", verifierServer.BaseUrl]).InvokeAsync();

            verifyResult.Should().Be(0);
            verifierServer.RequestedPaths.Should().Contain($"sessions/{sessionId}/receipts");
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
    /// Verifies that live verifier cross-check fails when packaged receipts are tampered.
    /// </summary>
    [Fact]
    public async Task VerifyCommand_WithServer_ShouldFailWhenPackagedReceiptsDoNotMatchRemote()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-verify-remote-mismatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();
        var sessionId = new AssessmentSessionId("verify-session-2");
        var remoteReceiptChain = new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = [CreateReceipt(sessionId, "stub-head")]
        };
        using var verifierServer = new StubVerifierServer((method, path) => path switch
        {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = sessionId.Value },
            var value when value == $"sessions/{sessionId}/checkpoints" && method == "POST" => CreateReceipt(sessionId, "stub-head"),
            var value when value == $"sessions/{sessionId}/receipts" && method == "GET" => remoteReceiptChain,
            _ => null
        });

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["session", "start", "--server", verifierServer.BaseUrl]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["session", "checkpoint"]).InvokeAsync()).Should().Be(0);
            var actualTimelineHeadHash = OwsEventChain.ReadLastEventHash(Path.Combine(projectRoot, ".ows", OwsConstants.TimelineFileName));
            remoteReceiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [CreateReceipt(sessionId, actualTimelineHeadHash)]
            };
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName),
                JsonSerializer.Serialize(remoteReceiptChain));
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.GetEntry(OwsConstants.ReceiptsFileName)!.Delete();
                var tamperedChain = new ReceiptChain
                {
                    SessionId = sessionId,
                    Receipts = [remoteReceiptChain.Receipts[0] with { ReceiptHash = "tampered-receipt-hash" }]
                };
                var entry = archive.CreateEntry(OwsConstants.ReceiptsFileName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(tamperedChain));
            }

            var verifyResult = await OwsCommandFactory.BuildRootCommand().Parse(["verify", "--server", verifierServer.BaseUrl]).InvokeAsync();

            verifyResult.Should().Be(1);
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
    /// Creates a stable test receipt for the supplied session.
    /// </summary>
    /// <param name="sessionId">The test session identifier.</param>
    /// <param name="timelineHeadHash">The timeline head hash anchored by the receipt.</param>
    /// <returns>The chained test receipt.</returns>
    private static CheckpointReceipt CreateReceipt(AssessmentSessionId sessionId, string timelineHeadHash) =>
        ReceiptChainVerifier.IssueReceipt(
            new Checkpoint
            {
                SessionId = sessionId,
                SequenceNumber = 1,
                TimelineHeadHash = timelineHeadHash
            },
            ReceiptChainVerifier.GenesisPreviousReceiptHash);
}
