using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ows.Core.Agent;

namespace Ows.Cli.Commands;

/// <summary>
/// Builds the diagnostic command that runs the local OWS Agent host.
/// </summary>
public static class AgentCommandBuilder {
    /// <summary>
    /// Builds <c>ows agent run</c>.
    /// </summary>
    /// <returns>The constructed <see cref="Command"/> representing the agent CLI command.</returns>
    public static Command Build() {
        var command = new Command("agent", "Run the local OWS Agent host.");
        var pollOption = new Option<bool>("--poll") {
            Description = "Use polling fallback for all registered projects."
        };
        var runCommand = new Command("run", "Watch all explicitly initialized registered projects until stopped.");
        runCommand.Options.Add(pollOption);
        runCommand.SetAction(async parseResult => {
                var usePolling = parseResult.GetValue(pollOption);
                using var cancellation = new CancellationTokenSource();
                var handler = CreateHandler(cancellation);
                Console.CancelKeyPress += handler;
                try {
                    await new OwsAgentHost(new OwsProjectRegistry(), usePolling).RunAsync(cancellation.Token);
                } finally {
                    Console.CancelKeyPress -= handler;
                }

                return 0;

                static ConsoleCancelEventHandler CreateHandler(CancellationTokenSource cts) => (_, args) => {
                    args.Cancel = true;
                    cts.Cancel();
                };
            }
        );
        command.Subcommands.Add(runCommand);

        var serviceCommand = new Command("service", "Run the OWS Agent under the operating system service host.");
        serviceCommand.SetAction(async _ => {
                var builder = Host.CreateApplicationBuilder();
                builder.Services.AddWindowsService(options => options.ServiceName = "OWS Agent");
                builder.Services.AddHostedService<WindowsAgentHostedService>();
                using var host = builder.Build();
                await host.RunAsync();
                return 0;
            }
        );
        command.Subcommands.Add(serviceCommand);
        return command;
    }

    /// <summary>
    /// Represents the <see cref="WindowsAgentHostedService"/> type.
    /// </summary>
    private sealed class WindowsAgentHostedService : BackgroundService {
        /// <summary>
        /// Asynchronously executes the background service by running the OwsAgentHost.
        /// </summary>
        /// <returns>A <see cref="Task"/> that represents the asynchronous execution operation.</returns>
        /// <param name="stoppingToken">Triggered when the background service is shut down or stopped.</param>
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            new OwsAgentHost(new OwsProjectRegistry()).RunAsync(stoppingToken);
    }
}
