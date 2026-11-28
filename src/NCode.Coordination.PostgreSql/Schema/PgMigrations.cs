// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Schema;

/// <summary>
/// The migrations of the schema, loaded from the assembly's embedded resources, and the runner that applies them.
/// </summary>
internal static partial class PgMigrations
{
    /// <summary>The start of a migration's manifest resource name.</summary>
    private const string ResourcePrefix = "NCode.Coordination.PostgreSql.Schema.Migrations.";

    /// <summary>The end of a migration's manifest resource name.</summary>
    private const string ResourceSuffix = ".sql";

    /// <summary>The start of the text that the advisory lock of a schema's migrations is taken on.</summary>
    private const string LockPrefix = "ncode-coordination:";

    /// <summary>The start of the statement that creates the schema, which is skipped when the schema exists.</summary>
    private const string CreateSchema = "CREATE SCHEMA";

    /// <summary>The statement that serializes the runners of one schema until the transaction ends.</summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))";

    /// <summary>The statement that finds whether the schema and its version table exist.</summary>
    private const string ExistsSql =
        "SELECT to_regnamespace($1) IS NOT NULL, to_regclass($2) IS NOT NULL";

    /// <summary>The statement that reads the highest version applied.</summary>
    private const string MaxVersionSql =
        "SELECT coalesce(max(version), 0) FROM {schema}.schema_version";

    /// <summary>The statement that reads the row of the highest version applied.</summary>
    private const string LatestRowSql =
        "SELECT version, compatible_from FROM {schema}.schema_version ORDER BY version DESC LIMIT 1";

    /// <summary>Matches the semicolon that ends a statement: the last one on its line.</summary>
    [GeneratedRegex(@";[ \t\r]*(?:\n|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex StatementEnd();

    /// <summary>The versions, names and compatibility of the migrations, in order.</summary>
    private static readonly (int Version, string Name, int CompatibleFrom)[] Table =
    [
        (1, "keyspaces", 1),
        (2, "counting", 1),
        (3, "records", 1),
        (4, "ownership", 1),
        (5, "notifications", 1),
    ];

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
                async (transaction, token) =>
                {
                    var exists = await transaction
                        .FirstAsync(
                            [
                                new PgStatement(
                                    "SELECT to_regclass($1) IS NOT NULL",
                                    schema.Quoted + ".schema_version"
                                ),
                            ],
                            async (results, t) =>
                            {
                                var rows = await results
                                    .ReadAsync(reader => reader.GetBoolean(0), t)
                                    .ConfigureAwait(false);
                                return rows[0];
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    if (!exists)
                    {
                        return (0, 0);
                    }

                    var latestSql = schema.Apply(LatestRowSql);
                    var latest = await transaction
                        .NextAsync(
                            [new PgStatement(latestSql)],
                            async (results, t) =>
                                await results
                                    .ReadAsync(
                                        reader => (reader.GetInt32(0), reader.GetInt32(1)),
                                        t
                                    )
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return latest.IsEmpty ? (0, 0) : latest[0];
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

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
                async (transaction, token) =>
                {
                    var (schemaExists, tableExists) = await transaction
                        .FirstAsync(
                            [
                                new PgStatement(LockSql, LockPrefix + schema.Name),
                                new PgStatement(
                                    ExistsSql,
                                    schema.Quoted,
                                    schema.Quoted + ".schema_version"
                                ),
                            ],
                            async (results, t) =>
                            {
                                await results.ReadAsync(_ => true, t).ConfigureAwait(false);
                                var rows = await results
                                    .ReadAsync(
                                        reader => (reader.GetBoolean(0), reader.GetBoolean(1)),
                                        t
                                    )
                                    .ConfigureAwait(false);
                                return rows[0];
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    var applied = 0;
                    if (tableExists)
                    {
                        var maxVersionSql = schema.Apply(MaxVersionSql);
                        var versions = await transaction
                            .NextAsync(
                                [new PgStatement(maxVersionSql)],
                                async (results, t) =>
                                    await results
                                        .ReadAsync(reader => reader.GetInt32(0), t)
                                        .ConfigureAwait(false),
                                token
                            )
                            .ConfigureAwait(false);
                        applied = versions[0];
                    }

                    if (applied >= migration.Version)
                    {
                        await transaction.RollbackAsync(token).ConfigureAwait(false);
                        return false;
                    }

                    var statements = migration
                        .Statements.Where(statement =>
                            !(
                                schemaExists
                                && statement.StartsWith(CreateSchema, StringComparison.Ordinal)
                            )
                        )
                        .Select(statement =>
                        {
                            var statementSql = schema.Apply(statement);
                            return new PgStatement(statementSql);
                        })
                        .ToList();

                    await transaction.CommitAsync(statements, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static ImmutableArray<PgMigration> Load()
    {
        var migrations = ImmutableArray.CreateBuilder<PgMigration>();
        foreach (var (version, name, compatibleFrom) in Table)
        {
            var script = ReadResource($"{version:D4}-{name}");
            migrations.Add(new PgMigration(version, name, compatibleFrom, Split(script)));
        }

        return migrations.ToImmutable();
    }

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
