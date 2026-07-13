using FluentAssertions;
using Ows.Core.Agent;
using Ows.Core.Init;
using Ows.Core.Packaging;

namespace Ows.Core.Tests;

/// <summary>
/// Tests explicit project registration and multi-project agent hosting.
/// </summary>
public sealed class OwsAgentHostTests {
    [Fact]
    public void Registry_ShouldBeIdempotentAndPruneMissingRoots() {
        var workspace = CreateDirectory();
        var projectRoot = Path.Combine(workspace, "project");
        Directory.CreateDirectory(projectRoot);
        var registry = new OwsProjectRegistry(Path.Combine(workspace, "projects.json"));

        try {
            registry.Register(projectRoot).Should().BeTrue();
            registry.Register(projectRoot).Should().BeFalse();
            registry.GetProjects().Should().ContainSingle();

            Directory.Delete(projectRoot, recursive: true);

            registry.RemoveMissingProjects().Should().Be(1);
            registry.GetProjects().Should().BeEmpty();
        } finally {
            DeleteDirectory(workspace);
        }
    }

    [Fact]
    public async Task Registry_ShouldRetryWhenTheRegistryLockIsHeld() {
        var workspace = CreateDirectory();
        var projectRoot = Path.Combine(workspace, "project");
        Directory.CreateDirectory(projectRoot);
        var registryPath = Path.Combine(workspace, "projects.json");
        var registry = new OwsProjectRegistry(registryPath);
        using var heldLock = File.Open(registryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None);
        var releaseTask = Task.Run(async () => {
            await Task.Delay(150);
            heldLock.Dispose();
        });

        try {
            registry.Register(projectRoot).Should().BeTrue();
            await releaseTask;
        } finally {
            heldLock.Dispose();
            DeleteDirectory(workspace);
        }
    }

    [Fact]
    public async Task AgentHost_ShouldWatchAllRegisteredInitializedProjectsUntilCancelled() {
        var workspace = CreateDirectory();
        var projectOne = Path.Combine(workspace, "one");
        var projectTwo = Path.Combine(workspace, "two");
        new OwsProjectInitializer().Initialize(projectOne);
        new OwsProjectInitializer().Initialize(projectTwo);
        var registry = new OwsProjectRegistry(Path.Combine(workspace, "projects.json"));
        registry.Register(projectOne);
        registry.Register(projectTwo);

        using var cancellation = new CancellationTokenSource();
        var hostTask = new OwsAgentHost(registry, usePolling: true).RunAsync(cancellation.Token);
        try {
            await WaitUntilAsync(() =>
                File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")) &&
                File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json")));
            (await OwsAgentIpcClient.TryPingAsync(registry.RegistryPath)).Should().BeTrue();
            (await OwsAgentIpcClient.TryFlushAsync(registry.RegistryPath, projectOne)).Should().BeTrue();
            var packagePath = Path.Combine(projectOne, "agent-active.owspkg");
            (await new OwsPackageBuilder().CreatePackageAsync(new PackageCreationRequest {
                ProjectRootPath = projectOne,
                OutputPackagePath = packagePath
            }, CancellationToken.None)).Created.Should().BeTrue();
        } finally {
            cancellation.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")).Should().BeFalse();
        File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json")).Should().BeFalse();
        (await OwsAgentIpcClient.TryPingAsync(registry.RegistryPath)).Should().BeFalse();

        using var restartedCancellation = new CancellationTokenSource();
        var restartedHostTask = new OwsAgentHost(registry, usePolling: true).RunAsync(restartedCancellation.Token);
        try {
            await WaitUntilAsync(() =>
                File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")) &&
                File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json")));
        } finally {
            restartedCancellation.Cancel();
            await restartedHostTask.WaitAsync(TimeSpan.FromSeconds(5));
            DeleteDirectory(workspace);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        for (var attempt = 0; attempt < 50; attempt++) {
            if (condition()) {
                return;
            }

            await Task.Delay(100);
        }

        condition().Should().BeTrue("the local agent did not start all registered project watchers");
    }

    private static string CreateDirectory() {
        var path = Path.Combine(Path.GetTempPath(), $"ows-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }
}
