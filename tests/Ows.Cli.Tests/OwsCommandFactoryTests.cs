using FluentAssertions;
using Ows.Cli;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests CLI command construction.
/// </summary>
public sealed class OwsCommandFactoryTests
{
    /// <summary>
    /// Verifies the placeholder command set.
    /// </summary>
    [Fact]
    public void BuildRootCommand_ShouldExposeExpectedSubcommands()
    {
        var rootCommand = OwsCommandFactory.BuildRootCommand();

        rootCommand.Subcommands.Select(command => command.Name).Should().BeEquivalentTo(
            ["init", "watch", "package", "verify", "report"]);
    }
}
