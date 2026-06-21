using System.Text;
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
public sealed class OwsSessionCommandTests {
    /// <summary>
    /// Verifies that session start creates persisted local session state.
    /// </summary>
    [Fact]
    public async Task SessionStart_ShouldCreateSessionAndReceiptsFiles() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        try {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(["session", "start"]).InvokeAsync();

            exitCode.Should().Be(0);
            File.Exists(Path.Combine(projectRoot, ".ows", "session.json")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName)).Should().BeTrue();
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that checkpoint appends a receipt for the current timeline head.
    /// </summary>
    [Fact]
    public async Task Checkpoint_ShouldAppendReceiptToReceiptsFile() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["session", "start"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(["session", "checkpoint"]).InvokeAsync();
            var receiptsPath = Path.Combine(projectRoot, ".ows", OwsConstants.ReceiptsFileName);
            var receiptChain = JsonSerializer.Deserialize<ReceiptChain>(File.ReadAllText(receiptsPath));

            exitCode.Should().Be(0);
            receiptChain.Should().NotBeNull();
            receiptChain!.Receipts.Should().ContainSingle();
            receiptChain.Receipts[0].TimelineHeadHash.Should().NotBeNullOrWhiteSpace();
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that session start can target a remote verifier and persist that choice locally.
    /// </summary>
    [Fact]
    public async Task SessionStart_WithServer_ShouldPersistVerifierUrl() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-remote-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        using var verifierServer = new StubVerifierServer((method, path) => path switch {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            var value when value == $"sessions/{RemoteSessionId}/checkpoints" && method == "POST" =>
                CreateRemoteReceipt(),
            var value when value == $"sessions/{RemoteSessionId}/receipts" && method == "GET" => new ReceiptChain {
                SessionId = RemoteSessionId,
                Receipts = [CreateRemoteReceipt()]
            },
            _ => null
        });

        try {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync();
            var sessionState = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, ".ows", "session.json")));

            exitCode.Should().Be(0);
            sessionState.RootElement.GetProperty("SessionId").GetString().Should().Be(RemoteSessionId.Value);
            sessionState.RootElement.GetProperty("VerifierUrl").GetString().Should().Be(verifierServer.BaseUrl);
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that session start honors the documented camelCase config file fields.
    /// </summary>
    [Fact]
    public async Task SessionStart_WithCamelCaseConfig_ShouldUseConfiguredVerifierAndContext() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-camel-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalApiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        using var verifierServer = new StubVerifierServer((method, path) => path switch {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            _ => null
        });

        try {
            Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", "test-api-key");
            Directory.SetCurrentDirectory(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);

            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var configPath = Path.Combine(localFolder, "config.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                {
                  "owsVersion": "0.1",
                  "projectRoot": ".",
                  "initializedAtUtc": "2026-06-20T00:00:00Z",
                  "verifierUrl": "__VERIFIER_URL__",
                  "institutionId": "institution-1",
                  "assessmentId": "assessment-1",
                  "studentUserId": "student-1",
                  "courseOfferingId": "offering-1",
                  "uploadEnabled": true
                }
                """.Replace("__VERIFIER_URL__", verifierServer.BaseUrl, StringComparison.Ordinal));

            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start"])
                .InvokeAsync();

            var sessionState = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, ".ows", "session.json")));

            exitCode.Should().Be(0);
            sessionState.RootElement.GetProperty("SessionId").GetString().Should().Be(RemoteSessionId.Value);
            sessionState.RootElement.GetProperty("VerifierUrl").GetString().Should().Be(verifierServer.BaseUrl);
            sessionState.RootElement.GetProperty("InstitutionId").GetString().Should().Be("institution-1");
            sessionState.RootElement.GetProperty("AssessmentId").GetString().Should().Be("assessment-1");
            sessionState.RootElement.GetProperty("StudentUserId").GetString().Should().Be("student-1");
            sessionState.RootElement.GetProperty("CourseOfferingId").GetString().Should().Be("offering-1");
        } finally {
            Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", originalApiKey);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that remote session start sends the configured verifier API key header.
    /// </summary>
    [Fact]
    public async Task SessionStart_WithServer_ShouldSendVerifierApiKeyHeader() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-remote-session-key-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalApiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        using var verifierServer = new StubVerifierServer((method, path) => path switch {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            _ => null
        });

        try {
            Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", "test-api-key");
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();

            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync();

            exitCode.Should().Be(0);
            verifierServer.RequestedHeaders[0]["X-OWS-Verifier-Key"].Should().Be("test-api-key");
        } finally {
            Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", originalApiKey);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that remote checkpointing appends the receipt chain returned by the verifier.
    /// </summary>
    [Fact]
    public async Task Checkpoint_WithServer_ShouldAppendReceiptToReceiptsFile() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-remote-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();
        using var verifierServer = new StubVerifierServer((method, path) => path switch {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            var value when value == $"sessions/{RemoteSessionId}/checkpoints" && method == "POST" =>
                CreateRemoteReceipt(),
            var value when value == $"sessions/{RemoteSessionId}/receipts" && method == "GET" => new ReceiptChain {
                SessionId = RemoteSessionId,
                Receipts = [CreateRemoteReceipt()]
            },
            _ => null
        });

        try {
            Directory.SetCurrentDirectory(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            (await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync()).Should().Be(0);

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
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that session heartbeat CLI command parses arguments and sends the request to the verifier.
    /// </summary>
    [Fact]
    public async Task SessionHeartbeat_ShouldSendRequestToVerifier() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-heartbeat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        using var verifierServer = new StubVerifierServer((method, path) => path switch {
            "sessions" when method == "POST" => new StartSessionResponse { SessionId = RemoteSessionId.Value },
            var p when p == $"sessions/{RemoteSessionId.Value}/heartbeat" && method == "POST" => new
                SessionHeartbeatResponse {
                ServerTime = DateTimeOffset.UtcNow,
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                SessionTrustState = "Verified",
                SessionHead = new SessionHeadResponse {
                    SessionId = RemoteSessionId.Value,
                    LastSequenceNumber = 0,
                    LastTimelineHeadHash = "genesis",
                    LastReceiptHash = "genesis-receipt"
                }
            },
            _ => null
        });

        try {
            Directory.SetCurrentDirectory(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "start", "--server", verifierServer.BaseUrl])
                .InvokeAsync()).Should().Be(0);

            // Now send a heartbeat using the CLI
            var exitCode = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "heartbeat"])
                .InvokeAsync();

            exitCode.Should().Be(0);
            verifierServer.RequestedPaths.Should().Contain($"sessions/{RemoteSessionId.Value}/heartbeat");

            // Test with server override
            var exitCodeWithOverride = await OwsCommandFactory.BuildRootCommand()
                .Parse(["session", "heartbeat", "--server", verifierServer.BaseUrl])
                .InvokeAsync();

            exitCodeWithOverride.Should().Be(0);
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
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
        new() {
            SessionId = RemoteSessionId,
            SequenceNumber = 1,
            TimelineHeadHash = "timeline-head-1",
            PreviousReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
            ReceiptHash = RemoteReceiptHash
        };
}
