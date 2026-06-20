using FluentAssertions;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

public sealed class PackageVerificationJobStoreTests
{
    [Fact]
    public async Task JsonJobStore_ShouldPreventDuplicateClaimsAcrossConcurrentWorkers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ows-job-store-{Guid.NewGuid():N}");
        var storePath = Path.Combine(tempDir, "jobs.json");

        try
        {
            var store = new JsonFilePackageVerificationJobStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            var queued = await store.QueueAsync("pkg-1", null, CancellationToken.None);

            var claims = await Task.WhenAll(
                store.TryStartNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None),
                store.TryStartNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None));

            claims.Count(claim => claim is not null).Should().Be(1);
            claims.Single(claim => claim is not null)!.Id.Should().Be(queued.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
