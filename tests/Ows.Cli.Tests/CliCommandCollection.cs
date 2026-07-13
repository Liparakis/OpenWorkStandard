using System;
using System.IO;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// A collection fixture that isolates CLI command tests from global machine-wide registry state.
/// </summary>
public class CliFixture : IDisposable {
    /// <summary>
    /// Stores the original value of the OWS_AGENT_REGISTRY_PATH environment variable.
    /// </summary>
    private readonly string? _originalRegistryPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="CliFixture"/> class.
    /// </summary>
    public CliFixture() {
        _originalRegistryPath = Environment.GetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH");
        var tempRegistryDir = Path.Combine(Path.GetTempPath(), $"ows-tests-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRegistryDir);
        var tempRegistryPath = Path.Combine(tempRegistryDir, "projects.json");
        Environment.SetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH", tempRegistryPath);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose() {
        Environment.SetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH", _originalRegistryPath);
    }
}

/// <summary>
/// Serializes CLI tests that change process-wide current directory state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliCommandCollection : ICollectionFixture<CliFixture> {
    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public const string Name = "CLI command tests";
}
