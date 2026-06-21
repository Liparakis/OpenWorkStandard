using System.Reflection;
using FluentAssertions;
using Ows.Core.Notarization;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the ordered PostgreSQL migration metadata used by the Work Verifier store.
/// </summary>
public sealed class PostgresVerifierMigratorTests {
    /// <summary>
    /// Verifies that the verifier migration list is ordered and contains the required durable tables.
    /// </summary>
    [Fact]
    public void GetMigrations_ShouldReturnOrderedDurableMigrations() {
        var getMigrationsMethod = typeof(PostgresVerifierMigrator).GetMethod(
            "GetMigrations",
            BindingFlags.Static | BindingFlags.NonPublic);

        getMigrationsMethod.Should().NotBeNull();
        var migrations = ((IEnumerable<object>) getMigrationsMethod!.Invoke(null, null)!).ToArray();

        migrations.Should().HaveCount(11);
        GetVersion(migrations[0]).Should().Be(1);
        GetName(migrations[0]).Should().Be("foundation");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_sessions");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_checkpoints");
        GetSql(migrations[0]).Should().Contain("create table if not exists verifier_audit_events");
        GetVersion(migrations[1]).Should().Be(2);
        GetName(migrations[1]).Should().Be("package-submissions");
        GetSql(migrations[1]).Should().Contain("create table if not exists verifier_package_submissions");
        GetVersion(migrations[2]).Should().Be(3);
        GetName(migrations[2]).Should().Be("package-session-anchors");
        GetSql(migrations[2]).Should().Contain("session_head_receipt_hash");
        GetVersion(migrations[3]).Should().Be(4);
        GetName(migrations[3]).Should().Be("package-idempotency");
        GetSql(migrations[3]).Should().Contain("idempotency_key");
        GetVersion(migrations[4]).Should().Be(5);
        GetName(migrations[4]).Should().Be("package-verification-results");
        GetSql(migrations[4]).Should().Contain("verification_result_json");
        GetVersion(migrations[5]).Should().Be(6);
        GetName(migrations[5]).Should().Be("session-leases");
        GetSql(migrations[5]).Should().Contain("last_heartbeat_at");
        GetSql(migrations[5]).Should().Contain("lease_expires_at");
        GetSql(migrations[5]).Should().Contain("has_lease_gap");
        GetVersion(migrations[6]).Should().Be(7);
        GetName(migrations[6]).Should().Be("education-wiring");
        GetSql(migrations[6]).Should().Contain("create table if not exists edu_institutions");
        GetSql(migrations[6]).Should().Contain("create table if not exists edu_assessments");
        GetSql(migrations[6]).Should().Contain("institution_id");
        GetSql(migrations[6]).Should().Contain("assessment_id");
        GetSql(migrations[6]).Should().Contain("student_user_id");
        GetVersion(migrations[7]).Should().Be(8);
        GetName(migrations[7]).Should().Be("api-keys");
        GetSql(migrations[7]).Should().Contain("create table if not exists verifier_api_keys");
        GetSql(migrations[7]).Should().Contain("key_hash");
        GetSql(migrations[7]).Should().Contain("last_used_at");
        GetVersion(migrations[8]).Should().Be(9);
        GetName(migrations[8]).Should().Be("package-intake-jobs");
        GetSql(migrations[8]).Should().Contain("create table if not exists ows_package_verification_jobs");
        GetSql(migrations[8]).Should().Contain("verification_job_id");
        GetSql(migrations[8]).Should().Contain("last_verification_error");
        GetVersion(migrations[9]).Should().Be(10);
        GetName(migrations[9]).Should().Be("student-client");
        GetSql(migrations[9]).Should().Contain("student_user_id");
        GetVersion(migrations[10]).Should().Be(11);
        GetName(migrations[10]).Should().Be("student-enrollment-model");
        GetSql(migrations[10]).Should().Contain("student_user_id");
        GetSql(migrations[10]).Should().Contain("drop column if exists role");
    }

    /// <summary>
    /// Reads the migration version through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration version.</returns>
    private static int GetVersion(object migration) =>
        (int) migration.GetType().GetProperty("Version")!.GetValue(migration)!;

    /// <summary>
    /// Reads the migration name through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration name.</returns>
    private static string GetName(object migration) =>
        (string) migration.GetType().GetProperty("Name")!.GetValue(migration)!;

    /// <summary>
    /// Reads the migration SQL through reflection.
    /// </summary>
    /// <param name="migration">The reflected migration instance.</param>
    /// <returns>The migration SQL batch.</returns>
    private static string GetSql(object migration) =>
        (string) migration.GetType().GetProperty("Sql")!.GetValue(migration)!;
}
