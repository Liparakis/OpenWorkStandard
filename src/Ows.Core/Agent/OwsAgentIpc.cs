using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Ows.Core.Agent;

/// <summary>
///     Provides the smallest local-only coordination channel for the OWS Agent.
/// </summary>
public static class OwsAgentIpcEndpoint {
    /// <summary>
    ///     Gets the platform endpoint derived from the registry path.
    /// </summary>
    /// <returns>A platform-specific named pipe name or Unix domain socket path.</returns>
    /// <param name="registryPath">The path to the project registry.</param>
    public static string Get(string registryPath) {
        var identity = Path.GetFullPath(registryPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        return OperatingSystem.IsWindows()
            ? $"ows-agent-{hash}"
            : Path.Combine(Path.GetDirectoryName(identity)!, $"agent-{hash}.sock");
    }
}

/// <summary>
///     Serves local Agent coordination requests. The service accepts only commands
///     that validate an explicitly registered initialized project; it never returns
///     project contents through this channel.
/// </summary>
public sealed class OwsAgentIpcServer {
    /// <summary>
    ///     JSON serialization options used for Web/CamelCase formatting.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The project registry to look up registered projects.
    /// </summary>
    private readonly OwsProjectRegistry _registry;

    /// <summary>
    ///     Initializes a local IPC server for the supplied project registry.
    /// </summary>
    /// <param name="registry">The project registry instance.</param>
    public OwsAgentIpcServer(OwsProjectRegistry registry) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    ///     Runs the local request loop until cancellation.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous server loop.</returns>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    public Task RunAsync(CancellationToken cancellationToken) {
        return OperatingSystem.IsWindows()
            ? RunNamedPipeAsync(cancellationToken)
            : RunUnixSocketAsync(cancellationToken);
    }

    /// <summary>
    ///     Runs the server loop using Windows Named Pipes.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the named pipe listener loop.</returns>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    [SupportedOSPlatform("windows")]
    private async Task RunNamedPipeAsync(CancellationToken cancellationToken) {
        var pipeName = OwsAgentIpcEndpoint.Get(_registry.RegistryPath);
        while (!cancellationToken.IsCancellationRequested) {
            await using var server = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                CreatePipeSecurity()
            );
            await server.WaitForConnectionAsync(cancellationToken);
            await HandleAsync(server, cancellationToken);
        }
    }

    /// <summary>
    ///     Creates the local security descriptor for the Windows Agent pipe.
    /// </summary>
    /// <returns>A security descriptor allowing local users to connect to the Agent.</returns>
    [SupportedOSPlatform("windows")]
    private static PipeSecurity CreatePipeSecurity() {
        var security = new PipeSecurity();
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow
            )
        );
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            )
        );
        return security;
    }

    /// <summary>
    ///     Runs the server loop using Unix Domain Sockets on non-Windows platforms.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the unix socket listener loop.</returns>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
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
                await using var stream = new NetworkStream(client, false);
                await HandleAsync(stream, cancellationToken);
            }
        } finally {
            if (File.Exists(socketPath)) {
                File.Delete(socketPath);
            }
        }
    }

    /// <summary>
    ///     Processes an incoming client connection stream.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the connection handler operation.</returns>
    /// <param name="stream">The connection stream.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
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

    /// <summary>
    ///     Checks if the project path is registered and initialized locally.
    /// </summary>
    /// <returns><see langword="true" /> if the project is registered and initialized; otherwise, <see langword="false" />.</returns>
    /// <param name="projectRootPath">The project root directory path.</param>
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
///     Provides best-effort client operations for the local OWS Agent channel.
/// </summary>
public static class OwsAgentIpcClient {
    /// <summary>
    ///     JSON serialization options used for client Web/CamelCase formatting.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Checks whether the local Agent is available for the registry.
    /// </summary>
    /// <returns>A task containing <see langword="true" /> if the ping succeeded; otherwise, <see langword="false" />.</returns>
    /// <param name="registryPath">The path to the project registry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<bool> TryPingAsync(string registryPath, CancellationToken cancellationToken = default) {
        return SendAsync(registryPath, "ping", null, cancellationToken);
    }

    /// <summary>
    ///     Requests a safe journal flush for a registered project.
    /// </summary>
    /// <returns>A task containing <see langword="true" /> if the flush request succeeded; otherwise, <see langword="false" />.</returns>
    /// <param name="projectRootPath">The project root path to flush.</param>
    /// <param name="registryPath">The path to the project registry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<bool> TryFlushAsync(
        string registryPath,
        string projectRootPath,
        CancellationToken cancellationToken = default
    ) {
        return SendAsync(registryPath, "flush", projectRootPath, cancellationToken);
    }

    /// <summary>
    ///     Sends an IPC command to the agent server.
    /// </summary>
    /// <returns>
    ///     A task containing <see langword="true" /> if the command succeeded and was accepted; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <param name="command">The command string to send.</param>
    /// <param name="registryPath">The path to the project registry.</param>
    /// <param name="projectRootPath">The project root path associated with the command, if any.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation.</param>
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

    /// <summary>
    ///     Connects to the platform-specific IPC channel.
    /// </summary>
    /// <returns>A <see cref="Task{Stream}" /> returning the connected stream.</returns>
    /// <param name="registryPath">The path to the project registry.</param>
    /// <param name="cancellationToken">A token to cancel the connection.</param>
    private static async Task<Stream> ConnectAsync(string registryPath, CancellationToken cancellationToken) {
        var endpoint = OwsAgentIpcEndpoint.Get(registryPath);
        if (OperatingSystem.IsWindows()) {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken);
        return new NetworkStream(socket, true);
    }

    private sealed record AgentIpcResponse(bool Accepted, string Message);
}
