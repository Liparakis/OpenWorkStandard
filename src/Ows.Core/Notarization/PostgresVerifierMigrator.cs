using Npgsql;

namespace Ows.Core.Notarization;

/// <summary>
/// Applies ordered PostgreSQL schema migrations for the Work Verifier store.
/// </summary>
public static class PostgresVerifierMigrator
{
    private const string CreateSchemaVersionTableSql = """
                                                       create table if not exists ows_verifier_schema_version (
                                                           version integer primary key,
                                                           name text not null,
                                                           applied_at timestamptz not null default now()
                                                       );
                                                       """;

    private const string Migration001FoundationSql = """
                                                     create table if not exists verifier_sessions (
                                                         id text primary key,
                                                         created_at timestamptz not null default now(),
                                                         client_id text null,
                                                         assessment_id text null,
                                                         metadata_json jsonb not null default '{}'::jsonb,
                                                         head_receipt_hash text not null default '',
                                                         head_event_hash text not null default '',
                                                         checkpoint_count integer not null default 0
                                                     );

                                                     create table if not exists verifier_checkpoints (
                                                         id bigserial primary key,
                                                         session_id text not null references verifier_sessions(id) on delete cascade,
                                                         sequence_number integer not null,
                                                         client_time timestamptz null,
                                                         server_time timestamptz not null,
                                                         previous_event_hash text not null,
                                                         current_event_hash text not null,
                                                         project_state_hash text not null,
                                                         previous_receipt_hash text not null,
                                                         receipt_hash text not null,
                                                         server_signature text not null default '',
                                                         idempotency_key text null,
                                                         created_at timestamptz not null default now(),
                                                         constraint uq_verifier_checkpoints_session_sequence unique (session_id, sequence_number),
                                                         constraint uq_verifier_checkpoints_session_receipt unique (session_id, receipt_hash)
                                                     );

                                                     create unique index if not exists ix_verifier_checkpoints_session_idempotency
                                                         on verifier_checkpoints (session_id, idempotency_key)
                                                         where idempotency_key is not null;

                                                     create table if not exists verifier_audit_events (
                                                         id bigserial primary key,
                                                         session_id text null references verifier_sessions(id) on delete cascade,
                                                         event_type text not null,
                                                         payload_json jsonb not null default '{}'::jsonb,
                                                         created_at timestamptz not null default now()
                                                     );
                                                     """;

    private const string Migration002PackageSubmissionsSql = """
                                                             create table if not exists verifier_package_submissions (
                                                                 id text primary key,
                                                                 session_id text null references verifier_sessions(id) on delete set null,
                                                                 object_storage_provider text not null,
                                                                 object_bucket text not null,
                                                                 object_key text not null,
                                                                 package_sha256 text not null,
                                                                 package_size_bytes bigint not null,
                                                                 verification_status text not null default 'Registered',
                                                                 trust_status text null,
                                                                 created_at timestamptz not null default now(),
                                                                 constraint uq_verifier_package_submissions_object unique (object_storage_provider, object_bucket, object_key),
                                                                 constraint ck_verifier_package_submissions_size check (package_size_bytes > 0),
                                                                 constraint ck_verifier_package_submissions_sha256 check (package_sha256 ~ '^[0-9a-fA-F]{64}$')
                                                             );
                                                             """;

    private const string Migration003PackageSessionAnchorsSql = """
                                                               alter table verifier_package_submissions
                                                                   add column if not exists session_head_receipt_hash text null,
                                                                   add column if not exists session_head_event_hash text null,
                                                                   add column if not exists session_checkpoint_count integer null;
                                                               """;

    private const string Migration004PackageIdempotencySql = """
                                                            alter table verifier_package_submissions
                                                                add column if not exists idempotency_key text null;

                                                            create unique index if not exists ix_verifier_package_submissions_idempotency
                                                                on verifier_package_submissions (idempotency_key)
                                                                where idempotency_key is not null;
                                                            """;

    /// <summary>
    /// Applies any missing ordered verifier schema migrations using a fresh data source.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the schema is fully migrated.</returns>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await MigrateAsync(dataSource, cancellationToken);
    }

    /// <summary>
    /// Applies any missing ordered verifier schema migrations using an existing data source.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the schema is fully migrated.</returns>
    public static async Task MigrateAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, CreateSchemaVersionTableSql, cancellationToken);

        var appliedVersions = await LoadAppliedVersionsAsync(connection, cancellationToken);
        foreach (var migration in GetMigrations())
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            await ExecuteAsync(connection, migration.Sql, cancellationToken);
            await RecordAppliedVersionAsync(connection, migration, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the ordered verifier schema migrations.
    /// </summary>
    /// <returns>The ordered migration list.</returns>
    private static IReadOnlyList<PostgresVerifierMigration> GetMigrations() =>
    [
        new(1, "foundation", Migration001FoundationSql),
        new(2, "package-submissions", Migration002PackageSubmissionsSql),
        new(3, "package-session-anchors", Migration003PackageSessionAnchorsSql),
        new(4, "package-idempotency", Migration004PackageIdempotencySql)
    ];

    /// <summary>
    /// Loads the migration versions already recorded for the verifier schema.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The applied migration versions.</returns>
    private static async Task<HashSet<int>> LoadAppliedVersionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select version
                              from ows_verifier_schema_version
                              order by version asc;
                              """;

        var versions = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }

        await reader.CloseAsync();
        return versions;
    }

    /// <summary>
    /// Records a successfully applied verifier schema migration.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="migration">The applied migration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task RecordAppliedVersionAsync(
        NpgsqlConnection connection,
        PostgresVerifierMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into ows_verifier_schema_version (version, name)
                              values (@version, @name);
                              """;
        command.Parameters.AddWithValue("version", migration.Version);
        command.Parameters.AddWithValue("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Executes a migration SQL batch.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="commandText">The SQL batch to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
