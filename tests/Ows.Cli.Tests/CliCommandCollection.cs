using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Serializes CLI tests that change process-wide current directory state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliCommandCollection
{
    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public const string Name = "CLI command tests";
}
