using System.Text.Json;
using System.Net;
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
        using var verifierServer = new StubVerifierServer();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync();
            var sessionState = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, ".ows", "session.json")));

            exitCode.Should().Be(0);
            sessionState.RootElement.GetProperty("SessionId").GetString().Should().Be(StubVerifierServer.SessionId.Value);
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
        using var verifierServer = new StubVerifierServer();

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
            receiptChain!.SessionId.Should().Be(StubVerifierServer.SessionId);
            receiptChain.Receipts.Should().ContainSingle();
            receiptChain.Receipts[0].ReceiptHash.Should().Be(StubVerifierServer.ReceiptHash);
            verifierServer.RequestedPaths.Should().ContainInOrder(
                "sessions",
                $"sessions/{StubVerifierServer.SessionId}/checkpoints",
                $"sessions/{StubVerifierServer.SessionId}/receipts");
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

    private sealed class StubVerifierServer : IDisposable
    {
        public static readonly AssessmentSessionId SessionId = new("remote-session-1");
        public const string ReceiptHash = "remote-receipt-1";

        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly HttpListener listener = new();
        private readonly Task listenerTask;

        public StubVerifierServer()
        {
            var port = GetAvailablePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(BaseUrl);
            listener.Start();
            listenerTask = Task.Run(Listen);
        }

        public string BaseUrl { get; }

        public List<string> RequestedPaths { get; } = [];

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();
            listener.Close();
            try
            {
                listenerTask.GetAwaiter().GetResult();
            }
            catch (HttpListenerException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Accepts stub verifier requests until the test disposes the listener.
        /// </summary>
        private void Listen()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                RequestedPaths.Add(context.Request.Url!.AbsolutePath.TrimStart('/'));
                WriteResponse(context);
            }
        }

        /// <summary>
        /// Writes the minimal verifier API responses needed by the CLI integration tests.
        /// </summary>
        /// <param name="context">The active HTTP listener context.</param>
        private static void WriteResponse(HttpListenerContext context)
        {
            var path = context.Request.Url!.AbsolutePath.TrimStart('/');
            var statusCode = HttpStatusCode.OK;
            object payload = path switch
            {
                "sessions" when context.Request.HttpMethod == "POST" => new StartSessionResponse { SessionId = SessionId.Value },
                var value when value == $"sessions/{SessionId}/checkpoints" && context.Request.HttpMethod == "POST" => new CheckpointReceipt
                {
                    SessionId = SessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = "timeline-head-1",
                    PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
                    ReceiptHash = ReceiptHash
                },
                var value when value == $"sessions/{SessionId}/receipts" && context.Request.HttpMethod == "GET" => new ReceiptChain
                {
                    SessionId = SessionId,
                    Receipts =
                    [
                        new CheckpointReceipt
                        {
                            SessionId = SessionId,
                            SequenceNumber = 1,
                            TimelineHeadHash = "timeline-head-1",
                            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
                            ReceiptHash = ReceiptHash
                        }
                    ]
                },
                _ => CreateNotFoundPayload()
            };
            var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            if (ReferenceEquals(payload, NotFoundPayload.Instance))
            {
                statusCode = HttpStatusCode.NotFound;
            }

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Finds a free loopback port for the stub listener.
        /// </summary>
        /// <returns>An available local TCP port.</returns>
        private static int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Returns the shared not-found payload used by the stub verifier.
        /// </summary>
        /// <returns>The shared payload sentinel.</returns>
        private static object CreateNotFoundPayload() => NotFoundPayload.Instance;

        private sealed class NotFoundPayload
        {
            public static readonly NotFoundPayload Instance = new();

            public string Error => "not-found";
        }
    }
}
