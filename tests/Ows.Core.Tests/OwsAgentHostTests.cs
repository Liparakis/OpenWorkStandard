using FluentAssertions;
using Ows.Core.Agent;
using Ows.Core.Init;
using Ows.Core.Packaging;

namespace Ows.Core.Tests;

/// <summary>
///     Tests explicit project registration and multi-project agent hosting.
/// </summary>
public sealed class OwsAgentHostTests {
    /// <summary>
    ///     Verifies that the project registry registration is idempotent and correctly prunes missing directory roots.
    /// </summary>
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

            Directory.Delete(projectRoot, true);

            registry.RemoveMissingProjects().Should().Be(1);
            registry.GetProjects().Should().BeEmpty();
        } finally {
            DeleteDirectory(workspace);
        }
    }

    /// <summary>
    ///     Verifies that the project registry registration retries and succeeds when the registry lock is temporarily held.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Registry_ShouldRetryWhenTheRegistryLockIsHeld() {
        var workspace = CreateDirectory();
        var projectRoot = Path.Combine(workspace, "project");
        Directory.CreateDirectory(projectRoot);
        var registryPath = Path.Combine(workspace, "projects.json");
        var registry = new OwsProjectRegistry(registryPath);
        using var heldLock = File.Open(
            registryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None
        );
        var releaseTask = Task.Run(async () => {
            await Task.Delay(150);
            heldLock.Dispose();
        }
        );

        try {
            registry.Register(projectRoot).Should().BeTrue();
            await releaseTask;
        } finally {
            heldLock.Dispose();
            DeleteDirectory(workspace);
        }
    }

    /// <summary>
    ///     Verifies that the agent host watches all registered and initialized projects until cancellation.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AgentHost_ShouldWatchAllRegisteredInitializedProjectsUntilCancelled() {
        var workspace = CreateDirectory();
        var projectOne = Path.Combine(workspace, "one");
        var projectTwo = Path.Combine(workspace, "two");
        OwsProjectInitializer.Initialize(projectOne);
        OwsProjectInitializer.Initialize(projectTwo);
        var registry = new OwsProjectRegistry(Path.Combine(workspace, "projects.json"));
        registry.Register(projectOne);
        registry.Register(projectTwo);

        using var cancellation = new CancellationTokenSource();
        var hostTask = new OwsAgentHost(registry, true).RunAsync(cancellation.Token);
        try {
            await WaitUntilAsync(() =>
                File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")) &&
                File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json"))
            );
            (await OwsAgentIpcClient.TryPingAsync(registry.RegistryPath)).Should().BeTrue();
            (await OwsAgentIpcClient.TryFlushAsync(registry.RegistryPath, projectOne)).Should().BeTrue();
            var packagePath = Path.Combine(projectOne, "agent-active.owspkg");
            (await OwsPackageBuilder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = projectOne,
                    OutputPackagePath = packagePath
                }, CancellationToken.None
            )).Created.Should().BeTrue();
        } finally {
            cancellation.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")).Should().BeFalse();
        File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json")).Should().BeFalse();
        (await OwsAgentIpcClient.TryPingAsync(registry.RegistryPath)).Should().BeFalse();

        using var restartedCancellation = new CancellationTokenSource();
        var restartedHostTask = new OwsAgentHost(registry, true).RunAsync(restartedCancellation.Token);
        try {
            await WaitUntilAsync(() =>
                File.Exists(Path.Combine(projectOne, OwsConstants.LocalFolderName, "watcher.json")) &&
                File.Exists(Path.Combine(projectTwo, OwsConstants.LocalFolderName, "watcher.json"))
            );
        } finally {
            restartedCancellation.Cancel();
            await restartedHostTask.WaitAsync(TimeSpan.FromSeconds(5));
            DeleteDirectory(workspace);
        }
    }

    /// <summary>
    ///     Helper method to asynchronously poll a condition until it becomes true.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the polling operation.</returns>
    /// <param name="condition">The condition function to evaluate.</param>
    private static async Task WaitUntilAsync(Func<bool> condition) {
        for (var attempt = 0; attempt < 50; attempt++) {
            if (condition()) {
                return;
            }

            await Task.Delay(100);
        }

        condition().Should().BeTrue("the local agent did not start all registered project watchers");
    }

    /// <summary>
    ///     Creates a unique temporary directory in the temp path.
    /// </summary>
    /// <returns>The path to the newly created directory.</returns>
    private static string CreateDirectory() {
        var path = Path.Combine(Path.GetTempPath(), $"ows-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Deletes the directory at the specified path if it exists.
    /// </summary>
    /// <param name="path">The path to the directory to delete.</param>
    private static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }
}
