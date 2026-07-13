using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Ows.Core.Agent;

namespace Ows.Setup;

/// <summary>
///     Represents the <see cref="Program" /> type.
/// </summary>
internal static class Program {
    /// <summary>
    ///     The name of the OWS Agent Windows Service.
    /// </summary>
    private const string ServiceName = "OwsAgent";

    /// <summary>
    ///     The display name of the OWS Agent Windows Service.
    /// </summary>
    private const string DisplayName = "OWS Agent";

    /// <summary>
    ///     The directory name where the OWS Agent program files are installed.
    /// </summary>
    private const string InstallDirectoryName = "Open Work Standard";

    /// <summary>
    ///     The filename of the installed OWS Agent executable.
    /// </summary>
    private const string AgentExecutableName = "OwsAgent.exe";

    /// <summary>
    ///     The directory containing the installed OWS CLI executable.
    /// </summary>
    private const string CliDirectoryName = "cli";

    /// <summary>
    ///     The embedded ZIP resource containing the published service payload.
    /// </summary>
    private const string PayloadResourceName = "ows-payload.zip";

    /// <summary>
    ///     The directory name where the OWS Agent shared data is stored.
    /// </summary>
    private const string DataDirectoryName = "OpenWorkStandard";

    /// <summary>
    ///     The timeout duration allowed for stopping the OWS Agent service.
    /// </summary>
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The entry point of the setup/installer program.
    /// </summary>
    /// <returns>The exit code of the program (0 for success, 1 for failure).</returns>
    /// <param name="args">Command-line arguments.</param>
    public static async Task<int> Main(string[] args) {
        try {
            if (args.Any(argument => string.Equals(argument, "--service", StringComparison.OrdinalIgnoreCase))) {
                return await RunServiceAsync();
            }

            if (args.Any(argument => string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase))) {
                Uninstall(
                    args.Any(argument => string.Equals(argument, "--purge-data", StringComparison.OrdinalIgnoreCase))
                );
                return 0;
            }

            Install();
            ShowMessage(
                "OWS Agent installed and running silently.\n\nIt is available in Services as 'OWS Agent'.",
                "Open Work Standard"
            );
            return 0;
        } catch (Exception exception) {
            ShowMessage($"OWS Setup failed:\n\n{exception.Message}", "Open Work Standard Setup Error", 0x00000010);
            return 1;
        }
    }

    /// <summary>
    ///     Runs the OWS Agent inside the application builder as a Windows Service.
    /// </summary>
    /// <returns>A task containing the exit code (always 0 on completion).</returns>
    private static async Task<int> RunServiceAsync() {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = DisplayName);
        builder.Services.AddHostedService<WindowsAgentHostedService>();
        using var host = builder.Build();
        await host.RunAsync();
        return 0;
    }

    /// <summary>
    ///     Installs the program files and registers/starts the Windows Service.
    /// </summary>
    private static void Install() {
        var installDirectory = GetInstallDirectory();
        var servicePath = Path.Combine(installDirectory, AgentExecutableName);

        RemoveService();
        if (Directory.Exists(installDirectory)) {
            Directory.Delete(installDirectory, true);
        }

        Directory.CreateDirectory(installDirectory);
        ExtractPayload(installDirectory);
        RegisterCliPath(installDirectory);
        PrepareSharedDataDirectory();
        CreateService(servicePath);
        RunTool("sc.exe", $"start {ServiceName}");
        RegisterUninstallEntry(servicePath);
    }

    /// <summary>
    ///     Stops and removes the Windows Service, deletes the uninstall registry entry, and removes files.
    /// </summary>
    /// <param name="purgeData">Whether the shared data folder should also be deleted.</param>
    private static void Uninstall(bool purgeData) {
        if (!purgeData) {
            var choice = MessageBox(
                IntPtr.Zero,
                "Remove the shared OWS Agent data too?\n\nYes removes the Agent registry. No preserves it. Cancel leaves OWS installed.",
                "Uninstall Open Work Standard",
                0x00000033
            );
            if (choice == 2) {
                return;
            }

            purgeData = choice == 6;
        }

        RemoveService();
        RemoveUninstallEntry();
        var installDirectory = GetInstallDirectory();
        RemoveCliPath(installDirectory);
        ScheduleRemoval(installDirectory, purgeData ? GetDataDirectory() : null);

        ShowMessage(
            purgeData
                ? "OWS Agent, installed files, and shared Agent data are being removed. Project .ows folders are not touched."
                : "OWS Agent and installed files are being removed. Shared Agent data is preserved.",
            "Open Work Standard"
        );
    }

    /// <summary>
    ///     Registers the program as a Windows Service using sc.exe.
    /// </summary>
    /// <param name="servicePath">The path to the installed executable.</param>
    private static void CreateService(string servicePath) {
        RunTool(
            "sc.exe", [
                "create", ServiceName,
                "binPath=", $"\"{servicePath}\" --service",
                "start=", "auto",
                "obj=", "LocalSystem",
                "DisplayName=", DisplayName
            ]
        );
        RunTool(
            "sc.exe", [
                "description", ServiceName,
                "Watches explicitly initialized Open Work Standard projects."
            ]
        );
        RunTool(
            "sc.exe", [
                "failure", ServiceName,
                "reset=", "86400",
                "actions=", "restart/5000/restart/30000/restart/60000"
            ]
        );
    }

    /// <summary>
    ///     Stops and deletes the OWS Agent Windows Service if it exists.
    /// </summary>
    private static void RemoveService() {
        var query = RunTool("sc.exe", $"query {ServiceName}", true);
        if (query.ExitCode != 0) {
            return;
        }

        var processId = GetServiceProcessId();
        var currentState = query.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);
        if (!currentState) {
            var stop = RunTool("sc.exe", $"stop {ServiceName}", true);
            if (stop.ExitCode != 0 && stop.ExitCode != 1062 &&
                !stop.Output.Contains("1062", StringComparison.OrdinalIgnoreCase) &&
                !stop.Error.Contains("1062", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Could not stop the OWS Agent service. {stop.Error.Trim()} {stop.Output.Trim()}".Trim()
                );
            }
        }

        var deadline = DateTime.UtcNow.Add(ServiceStopTimeout);
        var stopped = false;
        while (DateTime.UtcNow < deadline) {
            var status = RunTool("sc.exe", $"query {ServiceName}", true);
            if (status.ExitCode != 0 || status.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) {
                stopped = true;
                break;
            }

            Thread.Sleep(250);
        }

        if (!stopped) {
            throw new InvalidOperationException(
                $"OWS Agent did not stop within {ServiceStopTimeout.TotalSeconds:0} seconds; installed files were left in place."
            );
        }

        WaitForProcessExit(processId);
        RunTool("sc.exe", $"delete {ServiceName}", true);
    }

    /// <summary>
    ///     Gets the process identifier currently owned by the Windows Service Control Manager service entry.
    /// </summary>
    /// <returns>The service process identifier, or null when no process is active.</returns>
    private static int? GetServiceProcessId() {
        var query = RunTool("sc.exe", $"queryex {ServiceName}", true);
        if (query.ExitCode != 0) {
            return null;
        }

        var match = Regex.Match(query.Output, @"PID\s*:\s*(?<pid>\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["pid"].Value, out var processId) && processId > 0
            ? processId
            : null;
    }

    /// <summary>
    ///     Waits for the stopped service process to release its executable and dependency files.
    /// </summary>
    /// <param name="processId">The process identifier captured before the service was stopped.</param>
    private static void WaitForProcessExit(int? processId) {
        if (processId is not > 0) {
            return;
        }

        var deadline = DateTime.UtcNow.Add(ServiceStopTimeout);
        while (DateTime.UtcNow < deadline) {
            try {
                using var process = Process.GetProcessById(processId.Value);
                if (process.HasExited) {
                    return;
                }
            } catch (ArgumentException) {
                return;
            } catch (InvalidOperationException) {
                return;
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException(
            $"OWS Agent process {processId.Value} did not exit within {ServiceStopTimeout.TotalSeconds:0} seconds; installed files were left in place."
        );
    }

    /// <summary>
    ///     Writes registry keys to create an entry in the Windows Programs and Features uninstall list.
    /// </summary>
    /// <param name="servicePath">The path to the installed executable.</param>
    private static void RegisterUninstallEntry(string servicePath) {
        using var key = Registry.LocalMachine.CreateSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenWorkStandard"
                        )
                        ?? throw new InvalidOperationException("Could not create the Windows uninstall entry.");
        var uninstallCommand = $"\"{servicePath}\" --uninstall";
        key.SetValue("DisplayName", "Open Work Standard");
        key.SetValue("DisplayVersion", "0.1.1");
        key.SetValue("Publisher", "Open Work Standard");
        key.SetValue("InstallLocation", GetInstallDirectory());
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    ///     Deletes the OWS Agent uninstall key tree from the Windows registry.
    /// </summary>
    private static void RemoveUninstallEntry() {
        Registry.LocalMachine.DeleteSubKeyTree(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenWorkStandard",
            false
        );
    }

    /// <summary>
    ///     Launches a background process that waits for this process to exit before deleting directories.
    /// </summary>
    /// <param name="installDirectory">The installation directory path.</param>
    /// <param name="dataDirectory">The data directory path, or null to preserve it.</param>
    private static void ScheduleRemoval(string installDirectory, string? dataDirectory) {
        var commands = new List<string> {
            "timeout /t 2 /nobreak >nul",
            $"rmdir /s /q \"{installDirectory}\""
        };
        if (dataDirectory is not null) {
            commands.Add($"rmdir /s /q \"{dataDirectory}\"");
        }

        Process.Start(
            new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = "/c " + string.Join(" & ", commands),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        );
    }

    /// <summary>
    ///     Creates the shared data directory and configures its access permissions.
    /// </summary>
    private static void PrepareSharedDataDirectory() {
        var dataDirectory = GetDataDirectory();
        Directory.CreateDirectory(dataDirectory);
        RunTool("icacls.exe", $"\"{dataDirectory}\" /grant *S-1-5-32-545:(OI)(CI)M /T");
    }

    /// <summary>
    ///     Extracts the embedded published application payload into the installation directory.
    /// </summary>
    /// <param name="installDirectory">The directory where the application files are installed.</param>
    private static void ExtractPayload(string installDirectory) {
        var root = Path.GetFullPath(installDirectory);
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
                            ?? throw new InvalidOperationException("The installer payload is missing.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries) {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("The installer payload contains an invalid path.");
            }

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
        }
    }

    /// <summary>
    ///     Adds the installed CLI directory to the machine PATH if it is not already present.
    /// </summary>
    /// <param name="installDirectory">The directory where the application files are installed.</param>
    private static void RegisterCliPath(string installDirectory) {
        var cliDirectory = Path.Combine(installDirectory, CliDirectoryName);
        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? string.Empty;
        var entries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(entry => string.Equals(
                    entry.TrimEnd('\\'), cliDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase
                )
            )) {
            return;
        }

        var updatedPath = string.IsNullOrWhiteSpace(machinePath)
            ? cliDirectory
            : machinePath.TrimEnd(';') + ";" + cliDirectory;
        Environment.SetEnvironmentVariable("Path", updatedPath, EnvironmentVariableTarget.Machine);
    }

    /// <summary>
    ///     Removes the installed CLI directory from the machine PATH.
    /// </summary>
    /// <param name="installDirectory">The directory where the application files were installed.</param>
    private static void RemoveCliPath(string installDirectory) {
        var cliDirectory = Path.Combine(installDirectory, CliDirectoryName);
        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? string.Empty;
        var entries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Where(entry => !string.Equals(
                                         entry.TrimEnd('\\'), cliDirectory.TrimEnd('\\'),
                                         StringComparison.OrdinalIgnoreCase
                                     )
                                 )
                                 .ToArray();
        Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.Machine);
    }

    /// <summary>
    ///     Gets the absolute installation path within Program Files.
    /// </summary>
    /// <returns>The absolute directory path string.</returns>
    private static string GetInstallDirectory() {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallDirectoryName);
    }

    /// <summary>
    ///     Gets the absolute shared data path within CommonApplicationData.
    /// </summary>
    /// <returns>The absolute directory path string.</returns>
    private static string GetDataDirectory() {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), DataDirectoryName
        );
    }

    /// <summary>
    ///     Launches a command-line tool synchronously with redirect inputs and returns the execution results.
    /// </summary>
    /// <returns>The execution result containing exit code, standard output, and standard error.</returns>
    /// <param name="fileName">The executable name or path.</param>
    /// <param name="arguments">The command-line argument string.</param>
    /// <param name="allowFailure">Whether a non-zero exit code is allowed without throwing an exception.</param>
    private static ToolResult RunTool(string fileName, string arguments, bool allowFailure = false) {
        return RunTool(
            new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }, allowFailure
        );
    }

    /// <summary>
    ///     Launches a command-line tool with a list of arguments and throws on failure if disallowed.
    /// </summary>
    /// <param name="fileName">The executable name or path.</param>
    /// <param name="arguments">The list of arguments to pass to the executable.</param>
    /// <param name="allowFailure">Whether a non-zero exit code is allowed without throwing an exception.</param>
    private static void RunTool(
        string fileName,
        IReadOnlyCollection<string> arguments,
        bool allowFailure = false
    ) {
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        RunTool(startInfo, allowFailure);
    }

    /// <summary>
    ///     Core helper that starts a process, reads output synchronously, waits for exit, and returns the result.
    /// </summary>
    /// <returns>The execution result containing exit code, standard output, and standard error.</returns>
    /// <param name="startInfo">The process start configuration.</param>
    /// <param name="allowFailure">Whether a non-zero exit code is allowed without throwing an exception.</param>
    private static ToolResult RunTool(ProcessStartInfo startInfo, bool allowFailure) {
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var result = new ToolResult(process.ExitCode, output, error);
        if (!allowFailure && result.ExitCode != 0) {
            throw new InvalidOperationException(
                $"{startInfo.FileName} failed: {result.Error.Trim()} {result.Output.Trim()}".Trim()
            );
        }

        return result;
    }

    /// <summary>
    ///     Shows a message box dialogue with the specified message and caption.
    /// </summary>
    /// <param name="message">The message text to display.</param>
    /// <param name="caption">The window title caption.</param>
    /// <param name="type">The message box window style/type.</param>
    private static void ShowMessage(string message, string caption, uint type = 0x00000040) {
        MessageBox(IntPtr.Zero, message, caption, type);
    }

    /// <summary>
    ///     Native Win32 MessageBox function from user32.dll.
    /// </summary>
    /// <returns>The button identifier clicked by the user.</returns>
    /// <param name="hWnd">Handle to the owner window.</param>
    /// <param name="text">The message to display.</param>
    /// <param name="caption">The dialog title.</param>
    /// <param name="type">The message box style flags.</param>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private sealed record ToolResult(int ExitCode, string Output, string Error);

    /// <summary>
    ///     Represents the <see cref="WindowsAgentHostedService" /> type.
    /// </summary>
    private sealed class WindowsAgentHostedService : BackgroundService {
        /// <summary>
        ///     Asynchronously executes the background hosted service by running the OWS Agent host.
        /// </summary>
        /// <returns>A <see cref="Task" /> representing the asynchronous service operation.</returns>
        /// <param name="stoppingToken">A token triggered when the background service is stopped.</param>
        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            return new OwsAgentHost(new OwsProjectRegistry()).RunAsync(stoppingToken);
        }
    }
}
