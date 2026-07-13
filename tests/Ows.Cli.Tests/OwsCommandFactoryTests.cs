using FluentAssertions;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests CLI command construction.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsCommandFactoryTests {
    /// <summary>
    /// Verifies the intentionally small local command set.
    /// </summary>
    [Fact]
    public void BuildRootCommand_ShouldExposeExpectedSubcommands() {
        var rootCommand = OwsCommandFactory.BuildRootCommand();

        rootCommand.Subcommands.Select(command => command.Name).Should().BeEquivalentTo(
            ["init", "agent", "status", "package", "verify", "inspect", "report"]);
        rootCommand.Subcommands.Single(command => command.Name == "package").Subcommands.Should().BeEmpty();
    }
}
