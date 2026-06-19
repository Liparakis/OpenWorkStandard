using System.Net;
using System.Text;
using System.Text.Json;

using Ows.Core.Notarization;

namespace Ows.Cli.Tests;

/// <summary>
/// Hosts a minimal in-process verifier API for CLI integration tests.
/// </summary>
internal sealed class StubVerifierServer : IDisposable
{
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly HttpListener listener = new();
    private readonly Task listenerTask;
    private readonly Func<string, string, object?> responder;

    /// <summary>
    /// Initializes a new stub verifier server.
    /// </summary>
    /// <param name="responder">Maps HTTP method and path to a JSON response payload, or <see langword="null"/> for 404.</param>
    public StubVerifierServer(Func<string, string, object?> responder)
    {
        this.responder = responder;
        var port = GetAvailablePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(BaseUrl);
        listener.Start();
        listenerTask = Task.Run(Listen);
    }

    /// <summary>
    /// Gets the loopback base URL for the stub verifier.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Gets the requested verifier paths in arrival order.
    /// </summary>
    public List<string> RequestedPaths { get; } = [];

    /// <summary>
    /// Gets the requested verifier headers in arrival order.
    /// </summary>
    public List<IReadOnlyDictionary<string, string>> RequestedHeaders { get; } = [];

    /// <inheritdoc />
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
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Accepts requests until the stub server is disposed.
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

            var path = context.Request.Url!.AbsolutePath.TrimStart('/');
            RequestedPaths.Add(path);
            RequestedHeaders.Add(ReadHeaders(context.Request));
            WriteResponse(context, path);
        }
    }

    /// <summary>
    /// Reads request headers into a stable dictionary for assertions.
    /// </summary>
    /// <param name="request">The active HTTP request.</param>
    /// <returns>The captured request headers.</returns>
    private static IReadOnlyDictionary<string, string> ReadHeaders(HttpListenerRequest request) =>
        request.Headers.AllKeys
            .Where(key => key is not null)
            .ToDictionary(key => key!, key => request.Headers[key!] ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the configured stub response.
    /// </summary>
    /// <param name="context">The active HTTP context.</param>
    /// <param name="path">The requested path.</param>
    private void WriteResponse(HttpListenerContext context, string path)
    {
        var payload = responder(context.Request.HttpMethod, path);
        var statusCode = payload is null ? HttpStatusCode.NotFound : HttpStatusCode.OK;
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload ?? new { Error = "not-found" }));

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    /// <summary>
    /// Finds an available loopback TCP port.
    /// </summary>
    /// <returns>The available port number.</returns>
    private static int GetAvailablePort()
    {
        var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }
}
