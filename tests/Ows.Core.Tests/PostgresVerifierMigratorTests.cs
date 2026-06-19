using FluentAssertions;
using Ows.Core.Notarization;
using System.Reflection;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the ordered PostgreSQL migration metadata used by the Work Verifier store.
/// </summary>
public sealed class PostgresVerifierMigratorTests
{
    /// <summary>
    /// Verifies that the verifier migration list is ordered and contains the required foundation tables.
    /// </summary>
    [Fact]
    public void GetMigrations_ShouldReturnOrderedFoundationMigration()
    {
        var getMigrationsMethod = typeof(PostgresVerifierMigrator).GetMethod(
            "GetMigrations",
            BindingFlags.Static | BindingFlags.NonPublic);

        getMigrationsMethod.Should().NotBeNull();
        var migrations = ((IEnumerable<object>)getMigrationsMethod!.Invoke(null, null)!).ToArray();

        migrations.Should().ContainSingle();
        GetVersion(migrations[0]).Should().Be(1);
        GetName(migrations[0]).Should().Be("foundation");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_sessions");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_checkpoints");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_audit_events");
    }

    /// <summary>
    /// Reads the migration version through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration version.</returns>
    private static int GetVersion(object migration) =>
        (int)migration.GetType().GetProperty("Version")!.GetValue(migration)!;

    /// <summary>
    /// Reads the migration name through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration name.</returns>
    private static string GetName(object migration) =>
        (string)migration.GetType().GetProperty("Name")!.GetValue(migration)!;

    /// <summary>
    /// Reads the migration SQL through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration SQL batch.</returns>
    private static string GetSql(object migration) =>
        (string)migration.GetType().GetProperty("Sql")!.GetValue(migration)!;
}
