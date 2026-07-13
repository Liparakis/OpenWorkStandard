using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ows.Core.Agent;

namespace Ows.Setup;

internal static class Program {
    private const string ServiceName = "OwsAgent";
    private const string DisplayName = "OWS Agent";
    private const string InstallDirectoryName = "Open Work Standard";
    private const string DataDirectoryName = "OpenWorkStandard";
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args) {
        try {
            if (args.Any(argument => string.Equals(argument, "--service", StringComparison.OrdinalIgnoreCase))) {
                return await RunServiceAsync(args);
            }

            if (args.Any(argument => string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase))) {
                Uninstall(args.Any(argument => string.Equals(argument, "--purge-data", StringComparison.OrdinalIgnoreCase)));
                return 0;
            }

            Install();
            ShowMessage("OWS Agent installed and running silently.\n\nIt is available in Services as 'OWS Agent'.", "Open Work Standard");
            return 0;
        } catch (Exception exception) {
            ShowMessage($"OWS Setup failed:\n\n{exception.Message}", "Open Work Standard Setup Error", 0x00000010);
            return 1;
        }
    }

    private static async Task<int> RunServiceAsync(string[] args) {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = DisplayName);
        builder.Services.AddHostedService<WindowsAgentHostedService>();
        using var host = builder.Build();
        await host.RunAsync();
        return 0;
    }

    private static void Install() {
        var sourcePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate Ows.Setup.exe.");
        var installDirectory = GetInstallDirectory();
        var servicePath = Path.Combine(installDirectory, "Ows.Setup.exe");

        RemoveLegacyScheduledTasks();
        RemoveService();
        if (Directory.Exists(installDirectory)) {
            Directory.Delete(installDirectory, recursive: true);
        }

        Directory.CreateDirectory(installDirectory);
        File.Copy(sourcePath, servicePath, overwrite: true);
        PrepareSharedDataDirectory();
        MigrateLegacyRegistry();
        CreateService(servicePath);
        RunTool("sc.exe", $"start {ServiceName}");
        RegisterUninstallEntry(servicePath);
    }

    private static void Uninstall(bool purgeData) {
        if (!purgeData) {
            var choice = MessageBox(
                IntPtr.Zero,
                "Remove the shared OWS Agent data too?\n\nYes removes the Agent registry. No preserves it. Cancel leaves OWS installed.",
                "Uninstall Open Work Standard",
                0x00000033);
            if (choice == 2) {
                return;
            }

            purgeData = choice == 6;
        }

        RemoveLegacyScheduledTasks();
        RemoveService();
        RemoveUninstallEntry();
        var installDirectory = GetInstallDirectory();
        ScheduleRemoval(installDirectory, purgeData ? GetDataDirectory() : null);

        ShowMessage(purgeData
            ? "OWS Agent, installed files, and shared Agent data are being removed. Project .ows folders are not touched."
            : "OWS Agent and installed files are being removed. Shared Agent data is preserved.", "Open Work Standard");
    }

    private static void CreateService(string servicePath) {
        RunTool("sc.exe", new[] {
            "create", ServiceName,
            "binPath=", $"\"{servicePath}\" --service",
            "start=", "auto",
            "obj=", "LocalSystem",
            "DisplayName=", DisplayName
        });
        RunTool("sc.exe", new[] {
            "description", ServiceName,
            "Watches explicitly initialized Open Work Standard projects."
        });
        RunTool("sc.exe", new[] {
            "failure", ServiceName,
            "reset=", "86400",
            "actions=", "restart/5000/restart/30000/restart/60000"
        });
    }

    private static void RemoveService() {
        var query = RunTool("sc.exe", $"query {ServiceName}", allowFailure: true);
        if (query.ExitCode != 0) {
            return;
        }

        var currentState = query.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);
        if (!currentState) {
            var stop = RunTool("sc.exe", $"stop {ServiceName}", allowFailure: true);
            if (stop.ExitCode != 0 && stop.ExitCode != 1062 &&
                !stop.Output.Contains("1062", StringComparison.OrdinalIgnoreCase) &&
                !stop.Error.Contains("1062", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Could not stop the OWS Agent service. {stop.Error.Trim()} {stop.Output.Trim()}".Trim());
            }
        }

        var deadline = DateTime.UtcNow.Add(ServiceStopTimeout);
        var stopped = false;
        while (DateTime.UtcNow < deadline) {
            var status = RunTool("sc.exe", $"query {ServiceName}", allowFailure: true);
            if (status.ExitCode != 0 || status.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) {
                stopped = true;
                break;
            }

            Thread.Sleep(250);
        }

        if (!stopped) {
            throw new InvalidOperationException(
                $"OWS Agent did not stop within {ServiceStopTimeout.TotalSeconds:0} seconds; installed files were left in place.");
        }

        RunTool("sc.exe", $"delete {ServiceName}", allowFailure: true);
    }

    private static void RemoveLegacyScheduledTasks() {
        foreach (var taskName in new[] { "OwsAgent", "OwsAgent.User" }) {
            RunTool("schtasks.exe", $"/delete /tn {taskName} /f", allowFailure: true);
        }
    }

    private static void RegisterUninstallEntry(string servicePath) {
        using var key = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenWorkStandard")
            ?? throw new InvalidOperationException("Could not create the Windows uninstall entry.");
        var uninstallCommand = $"\"{servicePath}\" --uninstall";
        key.SetValue("DisplayName", "Open Work Standard");
        key.SetValue("DisplayVersion", "0.1.0");
        key.SetValue("Publisher", "Open Work Standard");
        key.SetValue("InstallLocation", GetInstallDirectory());
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void RemoveUninstallEntry() {
        Registry.LocalMachine.DeleteSubKeyTree(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenWorkStandard",
            throwOnMissingSubKey: false);
    }

    private static void ScheduleRemoval(string installDirectory, string? dataDirectory) {
        var commands = new List<string> {
            "timeout /t 2 /nobreak >nul",
            $"rmdir /s /q \"{installDirectory}\""
        };
        if (dataDirectory is not null) {
            commands.Add($"rmdir /s /q \"{dataDirectory}\"");
        }

        Process.Start(new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = "/c " + string.Join(" & ", commands),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void PrepareSharedDataDirectory() {
        var dataDirectory = GetDataDirectory();
        Directory.CreateDirectory(dataDirectory);
        RunTool("icacls.exe", $"\"{dataDirectory}\" /grant *S-1-5-32-545:(OI)(CI)M /T");
    }

    private static void MigrateLegacyRegistry() {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataDirectoryName,
            "projects.json");
        if (!File.Exists(legacyPath)) {
            return;
        }

        var source = new OwsProjectRegistry(legacyPath);
        var destination = new OwsProjectRegistry();
        foreach (var project in source.GetProjects()) {
            destination.Register(project.ProjectRootPath);
        }
    }

    private static string GetInstallDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallDirectoryName);

    private static string GetDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), DataDirectoryName);

    private static ToolResult RunTool(string fileName, string arguments, bool allowFailure = false) {
        return RunTool(new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }, allowFailure);
    }

    private static ToolResult RunTool(string fileName, IReadOnlyCollection<string> arguments, bool allowFailure = false) {
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

        return RunTool(startInfo, allowFailure);
    }

    private static ToolResult RunTool(ProcessStartInfo startInfo, bool allowFailure) {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var result = new ToolResult(process.ExitCode, output, error);
        if (!allowFailure && result.ExitCode != 0) {
            throw new InvalidOperationException($"{startInfo.FileName} failed: {result.Error.Trim()} {result.Output.Trim()}".Trim());
        }

        return result;
    }

    private static void ShowMessage(string message, string caption, uint type = 0x00000040) =>
        MessageBox(IntPtr.Zero, message, caption, type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private sealed record ToolResult(int ExitCode, string Output, string Error);

    private sealed class WindowsAgentHostedService : BackgroundService {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            new OwsAgentHost(new OwsProjectRegistry()).RunAsync(stoppingToken);
    }
}
