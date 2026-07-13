using FluentAssertions;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests CLI command construction.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsCommandFactoryTests {
    /// <summary>
    /// Verifies the placeholder command set.
    /// </summary>
    [Fact]
    public void BuildRootCommand_ShouldExposeExpectedSubcommands() {
        var rootCommand = OwsCommandFactory.BuildRootCommand();

        rootCommand.Subcommands.Select(command => command.Name).Should().BeEquivalentTo(
            ["init", "agent", "status", "session", "watch", "package", "verify", "inspect", "report", "event"]);
        rootCommand.Subcommands.Single(command => command.Name == "session").Hidden.Should().BeTrue();
        rootCommand.Subcommands.Single(command => command.Name == "watch").Hidden.Should().BeTrue();
        rootCommand.Subcommands.Single(command => command.Name == "event").Hidden.Should().BeTrue();
        rootCommand.Subcommands.Single(command => command.Name == "package").Subcommands
            .Single(command => command.Name == "upload").Hidden.Should().BeTrue();
    }
}
