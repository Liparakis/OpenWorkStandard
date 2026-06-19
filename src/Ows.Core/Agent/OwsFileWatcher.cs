using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Ows.Core.Agent;

/// <summary>
/// Watches a tracked project directory for file-system changes and yields debounced
/// <see cref="FileWatchEvent"/> records.
/// </summary>
/// <remarks>
/// <para>
/// Two complementary strategies run concurrently depending on configuration:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Native watcher</b> — wraps <see cref="FileSystemWatcher"/> for low-latency OS signals.
///       Used by default on Windows and macOS.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Polling fallback</b> — periodically scans the project tree and compares file sizes and
///       last-write times against a baseline snapshot. Used when
///       <see cref="FileWatcherOptions.UsePollingFallback"/> is <see langword="true"/> or when the
///       native watcher cannot be started. Also acts as a safety net alongside the native watcher to
///       catch events that OS signals can miss on network drives.
///     </description>
///   </item>
/// </list>
/// <para>
/// Both strategies write raw notifications into a shared <see cref="Channel{T}"/>. A debounce loop
/// drains the channel and withholds events until the configured quiet-time window elapses, then
/// emits one <see cref="FileWatchEvent"/> per affected path.
/// </para>
/// </remarks>
public sealed class OwsFileWatcher : IAsyncDisposable
{
    private readonly string _projectRoot;
    private readonly Func<string, bool> _shouldExclude;
    private readonly FileWatcherOptions _options;

    /// <summary>
    /// Initializes a new <see cref="OwsFileWatcher"/>.
    /// </summary>
    /// <param name="projectRoot">Absolute path to the tracked project root.</param>
    /// <param name="shouldExclude">
    /// Predicate receiving the <b>absolute</b> file path; returns <see langword="true"/> when the
    /// path should be ignored (e.g. paths inside <c>.ows/</c>).
    /// </param>
    /// <param name="options">Runtime options for debounce timing and polling behaviour.</param>
    public OwsFileWatcher(string projectRoot, Func<string, bool> shouldExclude, FileWatcherOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(shouldExclude);
        ArgumentNullException.ThrowIfNull(options);

        _projectRoot = projectRoot;
        _shouldExclude = shouldExclude;
        _options = options;
    }

    /// <summary>
    /// Streams debounced <see cref="FileWatchEvent"/> records until <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token that stops the watcher when cancelled.</param>
    /// <returns>An async sequence of debounced file-watch events.</returns>
    public async IAsyncEnumerable<FileWatchEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Unbounded channel — producers (OS watcher + poll loop) never block.
        var rawChannel = Channel.CreateUnbounded<(string AbsolutePath, FileChangeKind Kind, DateTimeOffset At)>(
            new UnboundedChannelOptions { SingleReader = true });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        // Launch producers in background.
        var producers = new List<Task>();
        if (!_options.UsePollingFallback)
        {
            producers.Add(RunNativeWatcherAsync(rawChannel.Writer, token));
        }

        producers.Add(RunPollingLoopAsync(rawChannel.Writer, token));

        // Complete the channel when all producers finish.
        _ = Task.WhenAll(producers).ContinueWith(_ => rawChannel.Writer.TryComplete(), TaskScheduler.Default);

        // pending maps relative path → (kind, first-observed-at).
        var pending =
            new Dictionary<string, (FileChangeKind Kind, DateTimeOffset At)>(StringComparer.OrdinalIgnoreCase);
        var shouldStop = false;

        while (!shouldStop)
        {
            // Try to read the next raw notification within the debounce window.
            bool readSucceeded = false;
            bool channelClosed = false;
            (string AbsolutePath, FileChangeKind Kind, DateTimeOffset At) raw = default;

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            delayCts.CancelAfter(_options.DebounceIntervalMs);

            try
            {
                raw = await rawChannel.Reader.ReadAsync(delayCts.Token);
                readSucceeded = true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Debounce window expired — flush pending events below.
            }
            catch (OperationCanceledException)
            {
                // Outer cancellation — flush pending then stop.
                shouldStop = true;
            }
            catch (ChannelClosedException)
            {
                // All producers finished — flush pending then stop.
                shouldStop = true;
                channelClosed = true;
            }

            if (readSucceeded)
            {
                // Accumulate debounced notifications.
                if (raw.AbsolutePath != null)
                {
                    var relative = Path.GetRelativePath(_projectRoot, raw.AbsolutePath);
                    if (!pending.TryGetValue(relative, out var existing))
                    {
                        pending[relative] = (raw.Kind, raw.At);
                    }
                    else
                    {
                        // Prefer Deleted if it arrives — it is always the final state.
                        var resolvedKind = raw.Kind == FileChangeKind.Deleted ? FileChangeKind.Deleted : existing.Kind;
                        pending[relative] = (resolvedKind, existing.At);
                    }
                }

                // Don't flush yet — keep accumulating until the debounce window expires.
                continue;
            }

            // Either the debounce window expired, the outer token was cancelled, or the channel
            // closed. In all cases, emit the accumulated events before stopping or looping.
            // C# forbids yield inside catch, so we collect into a local list and yield here.
            var toEmit = new List<FileWatchEvent>(pending.Count);
            foreach (var kv in pending)
            {
                toEmit.Add(new FileWatchEvent(kv.Key, kv.Value.Kind, kv.Value.At));
            }

            pending.Clear();

            foreach (var ev in toEmit)
            {
                yield return ev;
            }

            _ = channelClosed; // suppress unused-variable warning
        }

        // Ensure producers are fully stopped before disposal.
        await linkedCts.CancelAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Native watcher producer ────────────────────────────────────────────────

    private async Task RunNativeWatcherAsync(
        ChannelWriter<(string, FileChangeKind, DateTimeOffset)> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var fsw = new FileSystemWatcher(_projectRoot);
            fsw.IncludeSubdirectories = true;
            fsw.NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size;
            fsw.EnableRaisingEvents = false;

            void Enqueue(string path, FileChangeKind kind)
            {
                if (_shouldExclude(path))
                {
                    return;
                }

                writer.TryWrite((path, kind, DateTimeOffset.UtcNow));
            }

            fsw.Created += (_, e) => Enqueue(e.FullPath, FileChangeKind.Created);
            fsw.Changed += (_, e) => Enqueue(e.FullPath, FileChangeKind.Modified);
            fsw.Deleted += (_, e) => Enqueue(e.FullPath, FileChangeKind.Deleted);
            fsw.Renamed += (_, e) =>
            {
                Enqueue(e.OldFullPath, FileChangeKind.Deleted);
                Enqueue(e.FullPath, FileChangeKind.Created);
            };
            fsw.Error += (_, _) =>
            {
                // Buffer overflow or access error — continue; the polling loop will compensate.
            };

            fsw.EnableRaisingEvents = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch (Exception)
        {
            // If the native watcher fails to start (e.g. permission issue), silently fall
            // back to the polling loop which is always running alongside.
        }
    }

    // ── Polling fallback producer ──────────────────────────────────────────────

    private async Task RunPollingLoopAsync(
        ChannelWriter<(string, FileChangeKind, DateTimeOffset)> writer,
        CancellationToken cancellationToken)
    {
        // Baseline snapshot: path → (length, lastWriteUtc).
        var baseline = TakeSnapshot();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.PollingIntervalMs, cancellationToken);

                var current = TakeSnapshot();
                var now = DateTimeOffset.UtcNow;

                // Detect creations and modifications.
                foreach (var (path, info) in current)
                {
                    if (!baseline.TryGetValue(path, out var prev))
                    {
                        writer.TryWrite((path, FileChangeKind.Created, now));
                    }
                    else if (prev.Length != info.Length || prev.LastWriteUtc != info.LastWriteUtc)
                    {
                        writer.TryWrite((path, FileChangeKind.Modified, now));
                    }
                }

                // Detect deletions.
                foreach (var path in baseline.Keys)
                {
                    if (!current.ContainsKey(path))
                    {
                        writer.TryWrite((path, FileChangeKind.Deleted, now));
                    }
                }

                baseline = current;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    private Dictionary<string, (long Length, DateTime LastWriteUtc)> TakeSnapshot()
    {
        var snapshot = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_projectRoot, "*", SearchOption.AllDirectories))
            {
                if (_shouldExclude(path))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    snapshot[path] = (info.Length, info.LastWriteTimeUtc);
                }
                catch (IOException)
                {
                    // File may have been deleted between enumeration and stat — skip.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Project root was removed — return empty snapshot.
        }

        return snapshot;
    }
}