// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Schema;

/// <summary>
/// The migrations of the schema, loaded from the assembly's embedded resources, and the runner that applies them.
/// </summary>
internal static partial class PgMigrations
{
    /// <summary>
    /// The start of a migration's manifest resource name.
    /// </summary>
    private const string ResourcePrefix = "NCode.Coordination.PostgreSql.Schema.Migrations.";

    /// <summary>
    /// The end of a migration's manifest resource name.
    /// </summary>
    private const string ResourceSuffix = ".sql";

    /// <summary>
    /// The start of the text that the advisory lock of a schema's migrations is taken on.
    /// </summary>
    private const string LockPrefix = "ncode-coordination:";

    /// <summary>
    /// The start of the statement that creates the schema, which is skipped when the schema exists.
    /// </summary>
    private const string CreateSchema = "CREATE SCHEMA";

    /// <summary>
    /// The statement that serializes the runners of one schema until the transaction ends.
    /// </summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))";

    /// <summary>
    /// The statement that finds whether the schema and its version table exist.
    /// </summary>
    private const string ExistsSql =
        "SELECT to_regnamespace($1) IS NOT NULL, to_regclass($2) IS NOT NULL";

    /// <summary>
    /// The statement that finds whether the version table exists.
    /// </summary>
    private const string VersionTableExistsSql = "SELECT to_regclass($1) IS NOT NULL";

    /// <summary>
    /// The statement that reads the highest version applied.
    /// </summary>
    private const string MaxVersionSql =
        "SELECT coalesce(max(version), 0) FROM {schema}.schema_version";

    /// <summary>
    /// The statement that reads the row of the highest version applied.
    /// </summary>
    private const string LatestRowSql =
        "SELECT version, compatible_from FROM {schema}.schema_version ORDER BY version DESC LIMIT 1";

    /// <summary>
    /// The versions, names and compatibility of the migrations, in order.
    /// </summary>
    private static (int Version, string Name, int CompatibleFrom)[] Table { get; } =
    [
        (1, "keyspaces", 1),
        (2, "counting", 1),
        (3, "records", 1),
        (4, "ownership", 1),
        (5, "notifications", 1),
    ];

    /// <summary>
    /// Matches the semicolon that ends a statement: the last one on its line.
    /// </summary>
    [GeneratedRegex(@";[ \t\r]*(?:\n|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex StatementEnd();

    /// <summary>
    /// Gets the migrations in order of their versions.
    /// </summary>
    public static ImmutableArray<PgMigration> All { get; } = Load();

    /// <summary>
    /// Gets the version of the schema once every migration is applied.
    /// </summary>
    public static int Latest => All[^1].Version;

    /// <summary>
    /// Gets the tables that the provider reads and writes, which a role needs privileges on.
    /// </summary>
    public static ImmutableArray<string> Tables { get; } =
    [
        "keyspaces",
        "occurrences",
        "counters",
        "sequences",
        "sets",
        "set_members",
        "records",
        "changes",
        "change_items",
        "sessions",
        "ended_sessions",
        "members",
        "grants",
        "fencing",
        "claims",
        "topics",
        "messages",
    ];

    /// <summary>
    /// Splits a script into its statements: each ends at a semicolon that ends a line.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <returns>The trimmed statements, without their semicolons and without empty pieces.</returns>
    /// <exception cref="ArgumentNullException">The script is null.</exception>
    public static ImmutableArray<string> Split(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return
        [
            .. StatementEnd()
                .Split(script)
                .Select(piece => piece.Trim())
                .Where(piece => piece.Length > 0),
        ];
    }

    /// <summary>
    /// Applies every migration that the schema lacks, each in its own transaction, after taking the schema's
    /// advisory lock so that concurrent runners apply each once.
    /// </summary>
    /// <param name="server">The server to migrate.</param>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the schema is at the latest version.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public static async ValueTask ApplyAsync(
        PgServer server,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(schema);

        foreach (var migration in All)
        {
            await ApplyOneAsync(server, schema, migration, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the version the schema is at, and the oldest code version that may run on it.
    /// </summary>
    /// <param name="server">The server to read.</param>
    /// <param name="schema">The schema to read.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The version and its compatibility, or <c>(0, 0)</c> when the schema has no version table.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public static async ValueTask<(int Version, int CompatibleFrom)> ReadVersionAsync(
        PgServer server,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(schema);

        return await server
            .TransactAsync(
                PgIsolation.RepeatableReadReadOnly,
                (transaction, token) => ReadVersionInAsync(transaction, schema, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the schema's version inside a snapshot transaction.
    /// </summary>
    /// <param name="transaction">The snapshot transaction.</param>
    /// <param name="schema">The schema to read.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The version and its compatibility, or <c>(0, 0)</c> when the schema has no version table.</returns>
    private static async ValueTask<(int Version, int CompatibleFrom)> ReadVersionInAsync(
        PgTransaction transaction,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        var versionTable = schema.Quoted + ".schema_version";
        var existsStatement = new PgStatement(VersionTableExistsSql, versionTable);
        var exists = await transaction
            .FirstAsync([existsStatement], ReadBooleanAsync, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return (0, 0);
        }

        var latestSql = schema.Apply(LatestRowSql);
        var latestStatement = new PgStatement(latestSql);
        var latest = await transaction
            .NextAsync([latestStatement], ReadLatestAsync, cancellationToken)
            .ConfigureAwait(false);

        return latest.IsEmpty ? (0, 0) : latest[0];
    }

    /// <summary>
    /// Applies one migration inside a read-committed transaction, unless the schema already has it.
    /// </summary>
    /// <param name="transaction">The read-committed transaction.</param>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="migration">The migration to apply.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>True when the migration was applied and committed; false when it was rolled back as applied
    /// already.</returns>
    private static async ValueTask<bool> ApplyInAsync(
        PgTransaction transaction,
        PgSchemaName schema,
        PgMigration migration,
        CancellationToken cancellationToken
    )
    {
        var (schemaExists, tableExists) = await LockAndFindAsync(
                transaction,
                schema,
                cancellationToken
            )
            .ConfigureAwait(false);

        var applied = tableExists
            ? await ReadAppliedVersionAsync(transaction, schema, cancellationToken)
                .ConfigureAwait(false)
            : 0;

        if (applied >= migration.Version)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var statements = BuildStatements(schema, migration, schemaExists);
        await transaction.CommitAsync(statements, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Takes the schema's advisory lock and finds whether the schema and its version table exist.
    /// </summary>
    /// <param name="transaction">The read-committed transaction, whose first batch this is.</param>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Whether the schema exists and whether its version table exists.</returns>
    private static async ValueTask<(bool SchemaExists, bool TableExists)> LockAndFindAsync(
        PgTransaction transaction,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        var lockText = LockPrefix + schema.Name;
        var versionTable = schema.Quoted + ".schema_version";
        var lockStatement = new PgStatement(LockSql, lockText);
        var existsStatement = new PgStatement(ExistsSql, schema.Quoted, versionTable);

        return await transaction
            .FirstAsync([lockStatement, existsStatement], ReadExistsAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the highest version that the schema has applied.
    /// </summary>
    /// <param name="transaction">The read-committed transaction.</param>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The highest version, or 0 when no migration is recorded.</returns>
    private static async ValueTask<int> ReadAppliedVersionAsync(
        PgTransaction transaction,
        PgSchemaName schema,
        CancellationToken cancellationToken
    )
    {
        var maxVersionSql = schema.Apply(MaxVersionSql);
        var maxVersionStatement = new PgStatement(maxVersionSql);
        var versions = await transaction
            .NextAsync([maxVersionStatement], ReadVersionsAsync, cancellationToken)
            .ConfigureAwait(false);

        return versions[0];
    }

    /// <summary>
    /// Builds the statements that apply a migration, with the schema's name in each.
    /// </summary>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="migration">The migration to apply.</param>
    /// <param name="schemaExists">Whether the schema exists, which skips the statement that creates it.</param>
    /// <returns>The statements, in order.</returns>
    private static List<PgStatement> BuildStatements(
        PgSchemaName schema,
        PgMigration migration,
        bool schemaExists
    )
    {
        var statements = new List<PgStatement>(migration.Statements.Length);

        foreach (var statement in migration.Statements)
        {
            if (schemaExists && statement.StartsWith(CreateSchema, StringComparison.Ordinal))
            {
                continue;
            }

            var statementSql = schema.Apply(statement);
            var pgStatement = new PgStatement(statementSql);
            statements.Add(pgStatement);
        }

        return statements;
    }

    /// <summary>
    /// Reads the single boolean that a statement selected.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The boolean.</returns>
    private static async ValueTask<bool> ReadBooleanAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results
            .ReadAsync(reader => reader.GetBoolean(0), cancellationToken)
            .ConfigureAwait(false);
        return rows[0];
    }

    /// <summary>
    /// Reads the result of <see cref="LockSql"/>, which is discarded, and the row that <see cref="ExistsSql"/>
    /// selected.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>Whether the schema exists and whether its version table exists.</returns>
    private static async ValueTask<(bool SchemaExists, bool TableExists)> ReadExistsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        await results.ReadAsync(_ => true, cancellationToken).ConfigureAwait(false);
        var rows = await results.ReadAsync(ReadExistsRow, cancellationToken).ConfigureAwait(false);
        return rows[0];
    }

    /// <summary>
    /// Reads the row that <see cref="ExistsSql"/> selected.
    /// </summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <returns>Whether the schema exists and whether its version table exists.</returns>
    private static (bool SchemaExists, bool TableExists) ReadExistsRow(NpgsqlDataReader reader)
    {
        var schemaExists = reader.GetBoolean(0);
        var tableExists = reader.GetBoolean(1);
        return (schemaExists, tableExists);
    }

    /// <summary>
    /// Reads the highest version that <see cref="MaxVersionSql"/> selected.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The versions: one.</returns>
    private static async ValueTask<ImmutableArray<int>> ReadVersionsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(reader => reader.GetInt32(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the row that <see cref="LatestRowSql"/> selected.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The row: a version and its compatibility, or none when no migration is recorded.</returns>
    private static async ValueTask<
        ImmutableArray<(int Version, int CompatibleFrom)>
    > ReadLatestAsync(PgResults results, CancellationToken cancellationToken)
    {
        return await results.ReadAsync(ReadLatestRow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a row of <see cref="LatestRowSql"/>.
    /// </summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <returns>The version and its compatibility.</returns>
    private static (int Version, int CompatibleFrom) ReadLatestRow(NpgsqlDataReader reader)
    {
        var version = reader.GetInt32(0);
        var compatibleFrom = reader.GetInt32(1);
        return (version, compatibleFrom);
    }

    /// <summary>
    /// Applies one migration in its own transaction.
    /// </summary>
    /// <param name="server">The server to migrate.</param>
    /// <param name="schema">The schema to migrate.</param>
    /// <param name="migration">The migration to apply.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the migration is applied or the schema already had it.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private static async ValueTask ApplyOneAsync(
        PgServer server,
        PgSchemaName schema,
        PgMigration migration,
        CancellationToken cancellationToken
    )
    {
        await server
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => ApplyInAsync(transaction, schema, migration, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every migration of <see cref="Table"/> from the embedded resources.
    /// </summary>
    /// <returns>The migrations, in order of their versions.</returns>
    private static ImmutableArray<PgMigration> Load()
    {
        var migrations = ImmutableArray.CreateBuilder<PgMigration>();
        foreach (var (version, name, compatibleFrom) in Table)
        {
            var script = ReadResource($"{version:D4}-{name}");
            var statements = Split(script);
            var migration = new PgMigration(version, name, compatibleFrom, statements);
            migrations.Add(migration);
        }

        return migrations.ToImmutable();
    }

    /// <summary>
    /// Reads the text of an embedded migration script.
    /// </summary>
    /// <param name="name">The script's name without its prefix and suffix, such as <c>0001-keyspaces</c>.</param>
    /// <returns>The script's text.</returns>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    private static string ReadResource(string name)
    {
        var resourceName = ResourcePrefix + name + ResourceSuffix;
        using var stream = typeof(PgMigrations).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{resourceName}' does not exist."
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
