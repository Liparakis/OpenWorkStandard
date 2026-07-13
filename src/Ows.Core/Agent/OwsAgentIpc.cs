using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ows.Core.Agent;

/// <summary>
/// Provides the smallest local-only coordination channel for the OWS Agent.
/// </summary>
public static class OwsAgentIpcEndpoint {
    /// <summary>
    /// Gets the platform endpoint derived from the registry path.
    /// </summary>
    public static string Get(string registryPath) {
        var identity = Path.GetFullPath(registryPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        return OperatingSystem.IsWindows()
            ? $"ows-agent-{hash}"
            : Path.Combine(Path.GetDirectoryName(identity)!, $"agent-{hash}.sock");
    }
}

/// <summary>
/// Serves local Agent coordination requests. The service accepts only commands
/// that validate an explicitly registered initialized project; it never returns
/// project contents through this channel.
/// </summary>
public sealed class OwsAgentIpcServer {
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly OwsProjectRegistry _registry;

    /// <summary>
    /// Initializes a local IPC server for the supplied project registry.
    /// </summary>
    public OwsAgentIpcServer(OwsProjectRegistry registry) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Runs the local request loop until cancellation.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? RunNamedPipeAsync(cancellationToken)
            : RunUnixSocketAsync(cancellationToken);

    private async Task RunNamedPipeAsync(CancellationToken cancellationToken) {
        var pipeName = OwsAgentIpcEndpoint.Get(_registry.RegistryPath);
        while (!cancellationToken.IsCancellationRequested) {
            await using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous
            );
            await server.WaitForConnectionAsync(cancellationToken);
            await HandleAsync(server, cancellationToken);
        }
    }

    private async Task RunUnixSocketAsync(CancellationToken cancellationToken) {
        var socketPath = OwsAgentIpcEndpoint.Get(_registry.RegistryPath);
        var directory = Path.GetDirectoryName(socketPath)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(socketPath)) {
            File.Delete(socketPath);
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()) {
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        try {
            while (!cancellationToken.IsCancellationRequested) {
                using var client = await listener.AcceptAsync(cancellationToken);
                await using var stream = new NetworkStream(client, ownsSocket: false);
                await HandleAsync(stream, cancellationToken);
            }
        } finally {
            if (File.Exists(socketPath)) {
                File.Delete(socketPath);
            }
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken cancellationToken) {
        using var reader = new StreamReader(stream, leaveOpen: true);
        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        var request = string.IsNullOrWhiteSpace(line)
            ? null
            : JsonSerializer.Deserialize<AgentIpcRequest>(line, SerializerOptions);
        var response = request?.Command switch {
            "ping" => new AgentIpcResponse(true, "ready"),
            "flush" when IsRegisteredInitializedProject(request.ProjectRootPath) =>
                new AgentIpcResponse(true, "flushed"),
            "flush" => new AgentIpcResponse(false, "project is not registered and initialized"),
            _ => new AgentIpcResponse(false, "unsupported agent command")
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions));
    }

    private bool IsRegisteredInitializedProject(string? projectRootPath) {
        if (string.IsNullOrWhiteSpace(projectRootPath)) {
            return false;
        }

        var normalizedPath = Path.GetFullPath(projectRootPath);
        return _registry.GetProjects().Any(project =>
                   string.Equals(
                       project.ProjectRootPath, normalizedPath,
                       OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                   )
               ) &&
               Directory.Exists(Path.Combine(normalizedPath, OwsConstants.LocalFolderName)) &&
               File.Exists(Path.Combine(normalizedPath, OwsConstants.LocalFolderName, "config.json"));
    }

    private sealed record AgentIpcRequest(string Command, string? ProjectRootPath);

    private sealed record AgentIpcResponse(bool Accepted, string Message);
}

/// <summary>
/// Provides best-effort client operations for the local OWS Agent channel.
/// </summary>
public static class OwsAgentIpcClient {
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Checks whether the local Agent is available for the registry.
    /// </summary>
    public static Task<bool> TryPingAsync(string registryPath, CancellationToken cancellationToken = default) =>
        SendAsync(registryPath, "ping", null, cancellationToken);

    /// <summary>
    /// Requests a safe journal flush for a registered project.
    /// </summary>
    public static Task<bool> TryFlushAsync(
        string registryPath,
        string projectRootPath,
        CancellationToken cancellationToken = default
    ) =>
        SendAsync(registryPath, "flush", projectRootPath, cancellationToken);

    private static async Task<bool> SendAsync(
        string registryPath,
        string command,
        string? projectRootPath,
        CancellationToken cancellationToken
    ) {
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await using var stream = await ConnectAsync(registryPath, timeout.Token);
            using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command, projectRootPath }, SerializerOptions));
            var response = await reader.ReadLineAsync(timeout.Token);
            return response is not null &&
                   JsonSerializer.Deserialize<AgentIpcResponse>(response, SerializerOptions)?.Accepted == true;
        } catch (IOException) {
            return false;
        } catch (SocketException) {
            return false;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    private static async Task<Stream> ConnectAsync(string registryPath, CancellationToken cancellationToken) {
        var endpoint = OwsAgentIpcEndpoint.Get(registryPath);
        if (OperatingSystem.IsWindows()) {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private sealed record AgentIpcResponse(bool Accepted, string Message);
}
