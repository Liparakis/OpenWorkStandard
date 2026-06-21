namespace Ows.Core.Notarization;

/// <summary>
/// Describes a single ordered PostgreSQL schema migration for the Work Verifier store.
/// </summary>
/// <param name="Version">The monotonically increasing migration version.</param>
/// <param name="Name">The human-readable migration name.</param>
/// <param name="Sql">The SQL batch applied for the migration.</param>
internal sealed record PostgresVerifierMigration(int Version, string Name, string Sql);
