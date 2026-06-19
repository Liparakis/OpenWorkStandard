using System.Text.Json;

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
}
