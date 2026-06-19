using FluentAssertions;

using Ows.Core;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the local no-network receipt transport used by the CLI.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class LocalReceiptTransportTests
{
    /// <summary>
    /// Verifies that starting a session through the local transport persists an empty receipt chain.
    /// </summary>
    [Fact]
    public async Task StartSessionAsync_ShouldPersistEmptyReceiptChain()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-transport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var transport = new LocalReceiptTransport(projectRoot);
            var sessionId = await transport.StartSessionAsync(CancellationToken.None);
            var receiptChain = await transport.GetReceiptsAsync(CancellationToken.None);

            sessionId.Value.Should().NotBeNullOrWhiteSpace();
            receiptChain.SessionId.Should().Be(sessionId);
            receiptChain.Receipts.Should().BeEmpty();
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
