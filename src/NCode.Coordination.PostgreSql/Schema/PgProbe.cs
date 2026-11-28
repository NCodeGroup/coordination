// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Schema;

/// <summary>
/// The probe that refuses a server, a schema or a role that the provider can't keep its declarations on.
/// </summary>
/// <param name="server">The server to probe.</param>
/// <param name="source">The source of the connections, whose connection string is checked.</param>
/// <param name="profile">The profile whose requirements the server must meet.</param>
/// <param name="settings">The settings of the provider.</param>
internal sealed class PgProbe(
    PgServer server,
    PgSource source,
    PostgreSqlProfile profile,
    PgSettings settings
)
{
    /// <summary>
    /// The oldest server version number that the provider supports: PostgreSQL 15.
    /// </summary>
    private const int MinimumVersion = 150000;

    /// <summary>
    /// The statement that reads the server's version, role and durability settings.
    /// </summary>
    private const string ServerSql = """
        SELECT current_setting('server_version_num')::integer,
               pg_is_in_recovery(),
               current_setting('fsync'),
               current_setting('synchronous_commit'),
               current_setting('synchronous_standby_names')
        """;

    /// <summary>
    /// The statement that finds whether the role may use the schema: it returns no row when the schema doesn't
    /// exist yet, which the version check refuses.
    /// </summary>
    private const string SchemaPrivilegeSql =
        "SELECT has_schema_privilege(n.oid, 'USAGE') FROM pg_catalog.pg_namespace n WHERE n.nspname = $1";

    /// <summary>
    /// The statement that lists the tables of the schema that the role lacks a privilege on: each of SELECT, INSERT,
    /// UPDATE and DELETE on the tables named by <c>$2</c>, and SELECT on <c>schema_version</c>. Each privilege is
    /// checked on its own, because a list of privileges is true when the role holds any one of them.
    /// </summary>
    private const string TablePrivilegesSql = """
        SELECT c.relname
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1
          AND c.relkind IN ('r', 'p')
          AND (
            (
              c.relname = ANY($2::text[])
              AND NOT (
                has_table_privilege(c.oid, 'SELECT')
                AND has_table_privilege(c.oid, 'INSERT')
                AND has_table_privilege(c.oid, 'UPDATE')
                AND has_table_privilege(c.oid, 'DELETE')
              )
            )
            OR (
              c.relname = 'schema_version'
              AND NOT has_table_privilege(c.oid, 'SELECT')
            )
          )
        ORDER BY c.relname
        """;

    /// <summary>
    /// Creates the exception that refuses a server, a schema or a role.
    /// </summary>
    /// <param name="message">The reason the probe refuses.</param>
    /// <returns>The exception, whose code is <c>unsupported</c> and whose outcome is known.</returns>
    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    /// <summary>
    /// Reads the row that <see cref="ServerSql"/> selected.
    /// </summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <returns>The server's version number, whether it is in recovery, and its fsync, synchronous_commit and
    /// synchronous_standby_names settings.</returns>
    private static (
        int Version,
        bool InRecovery,
        string Fsync,
        string Commit,
        string Names
    ) ReadServerRow(NpgsqlDataReader reader)
    {
        var version = reader.GetInt32(0);
        var inRecovery = reader.GetBoolean(1);
        var fsync = reader.GetString(2);
        var commit = reader.GetString(3);
        var names = reader.GetString(4);
        return (version, inRecovery, fsync, commit, names);
    }

    /// <summary>
    /// Reads the batch of <see cref="ServerSql"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The server's version number, whether it is in recovery, and its fsync, synchronous_commit and
    /// synchronous_standby_names settings.</returns>
    private static async ValueTask<(
        int Version,
        bool InRecovery,
        string Fsync,
        string Commit,
        string Names
    )> ReadServerAsync(PgResults results, CancellationToken cancellationToken)
    {
        var rows = await results.ReadAsync(ReadServerRow, cancellationToken).ConfigureAwait(false);
        return rows[0];
    }

    /// <summary>
    /// Reads the batch of <see cref="SchemaPrivilegeSql"/> and <see cref="TablePrivilegesSql"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>Whether the role may use the schema, which is empty when the schema doesn't exist, and the tables
    /// that the role lacks a privilege on.</returns>
    private static async ValueTask<(
        ImmutableArray<bool> Usage,
        ImmutableArray<string> Denied
    )> ReadPrivilegesAsync(PgResults results, CancellationToken cancellationToken)
    {
        var usageRows = await results
            .ReadAsync(reader => reader.GetBoolean(0), cancellationToken)
            .ConfigureAwait(false);
        var deniedRows = await results
            .ReadAsync(reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);
        return (usageRows, deniedRows);
    }

    /// <summary>
    /// Refuses a server older than <see cref="MinimumVersion"/> and a server that is a standby.
    /// </summary>
    /// <param name="version">The server's version number.</param>
    /// <param name="inRecovery">Whether the server is in recovery.</param>
    /// <exception cref="CoordinationException">The server is refused.</exception>
    private static void CheckVersionAndRole(int version, bool inRecovery)
    {
        if (version < MinimumVersion)
        {
            throw Unsupported(
                $"The provider needs PostgreSQL 15 or later, and the server reports {version}."
            );
        }

        if (inRecovery)
        {
            throw Unsupported(
                "The server is a standby in recovery; the provider needs the primary."
            );
        }
    }

    /// <summary>
    /// Refuses a server whose fsync or synchronous_commit setting lets a crash lose acknowledged commits.
    /// </summary>
    /// <param name="fsync">The server's fsync setting.</param>
    /// <param name="commit">The synchronous_commit setting that the provider's sessions see.</param>
    /// <exception cref="CoordinationException">The server is refused.</exception>
    private static void CheckCommitsAreDurable(string fsync, string commit)
    {
        if (!string.Equals(fsync, "on", StringComparison.OrdinalIgnoreCase))
        {
            throw Unsupported(
                "The server runs with fsync off, so a crash loses commits it acknowledged."
            );
        }

        if (string.Equals(commit, "off", StringComparison.OrdinalIgnoreCase))
        {
            throw Unsupported(
                "The provider's sessions see synchronous_commit off, so a crash loses commits it acknowledged."
            );
        }
    }

    /// <summary>
    /// Gets the server to probe.
    /// </summary>
    private PgServer Server { get; } = server;

    /// <summary>
    /// Gets the source of the connections, whose connection string is checked.
    /// </summary>
    private PgSource Source { get; } = source;

    /// <summary>
    /// Gets the profile whose requirements the server must meet.
    /// </summary>
    private PostgreSqlProfile Profile { get; } = profile;

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    private PgSettings Settings { get; } = settings;

    /// <summary>
    /// Runs the probe: refuses what the provider can't use, and applies the migrations when the settings say so.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the probe.</param>
    /// <returns>A task that completes when the server, the schema and the role are usable.</returns>
    /// <exception cref="CoordinationException">
    /// The probe refused the server, the schema or the role, or the server could not be reached or failed.
    /// </exception>
    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Source.ConnectionString);

        if (connectionString.Multiplexing)
        {
            throw Unsupported(
                "The provider's data source multiplexes commands, which its listening connection and SET LOCAL can't share; turn Multiplexing off."
            );
        }

        if (connectionString.KeepAlive == 0)
        {
            throw Unsupported(
                "The provider's data source has no Keep Alive, which its listening connection needs to notice a server that went away; set Keep Alive, such as 15 seconds."
            );
        }

        await CheckServerAsync(cancellationToken).ConfigureAwait(false);

        if (Settings.ApplyMigrations)
        {
            await PgMigrations
                .ApplyAsync(Server, Settings.Schema, cancellationToken)
                .ConfigureAwait(false);
        }

        await CheckPrivilegesAsync(cancellationToken).ConfigureAwait(false);
        await CheckVersionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a server that is too old, is a standby, or can lose the commits it acknowledged.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the check.</param>
    /// <returns>A task that completes when the server is acceptable.</returns>
    /// <exception cref="CoordinationException">The server is refused, or could not be reached or failed.</exception>
    private async ValueTask CheckServerAsync(CancellationToken cancellationToken)
    {
        var statement = new PgStatement(ServerSql);
        var (version, inRecovery, fsync, commit, names) = await Server
            .ExecuteAsync([statement], ReadServerAsync, cancellationToken)
            .ConfigureAwait(false);

        CheckVersionAndRole(version, inRecovery);
        CheckCommitsAreDurable(fsync, commit);
        CheckSynchronousStandby(commit, names);
    }

    /// <summary>
    /// Refuses a server without a synchronous standby when the profile requires one.
    /// </summary>
    /// <param name="commit">The synchronous_commit setting that the provider's sessions see.</param>
    /// <param name="names">The server's synchronous_standby_names setting.</param>
    /// <exception cref="CoordinationException">The server is refused.</exception>
    private void CheckSynchronousStandby(string commit, string names)
    {
        if (!Profile.RequiresSynchronousStandby)
        {
            return;
        }

        var commitWaitsForStandby =
            string.Equals(commit, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commit, "remote_apply", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(names) || !commitWaitsForStandby)
        {
            throw Unsupported(
                $"The '{Profile.Name}' profile needs synchronous_standby_names to name a standby and synchronous_commit on or remote_apply, and the provider's sessions see '{names}' and '{commit}'."
            );
        }
    }

    /// <summary>
    /// Refuses a schema that is missing, older than the provider's code, or newer than it can run on.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the check.</param>
    /// <returns>A task that completes when the schema's version is acceptable.</returns>
    /// <exception cref="CoordinationException">The schema is refused, or the server could not be reached or failed.</exception>
    private async ValueTask CheckVersionAsync(CancellationToken cancellationToken)
    {
        var (version, compatibleFrom) = await PgMigrations
            .ReadVersionAsync(Server, Settings.Schema, cancellationToken)
            .ConfigureAwait(false);

        var latest = PgMigrations.Latest;
        var name = Settings.Schema.Name;

        if (version == 0)
        {
            throw Unsupported(
                $"Schema '{name}' has no version, and the provider's code is at version {latest}. Apply its migrations, which PostgreSqlSchema.GetMigrations returns, or turn ApplyMigrations on."
            );
        }

        if (version < latest)
        {
            throw Unsupported(
                $"Schema '{name}' is at version {version}, and the provider's code is at version {latest}. Apply the migrations after {version}, which PostgreSqlSchema.GetMigrations returns, or turn ApplyMigrations on."
            );
        }

        if (version > latest && compatibleFrom > latest)
        {
            throw Unsupported(
                $"Schema '{name}' is at version {version}, which needs code from version {compatibleFrom}, and the provider's code is at version {latest}. Upgrade the provider; PostgreSqlSchema.GetMigrations returns the migrations this code knows."
            );
        }
    }

    /// <summary>
    /// Refuses a role that lacks USAGE on the schema or a privilege on one of its tables.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the check.</param>
    /// <returns>A task that completes when the role's privileges suffice.</returns>
    /// <exception cref="CoordinationException">The role is refused, or the server could not be reached or failed.</exception>
    private async ValueTask CheckPrivilegesAsync(CancellationToken cancellationToken)
    {
        var name = Settings.Schema.Name;
        var tables = PgMigrations.Tables.ToArray();

        var tablesArgument = PgArgument.Of(tables);
        var schemaStatement = new PgStatement(SchemaPrivilegeSql, name);
        var tablesStatement = new PgStatement(TablePrivilegesSql, name, tablesArgument);

        var (usage, denied) = await Server
            .ExecuteAsync(
                [schemaStatement, tablesStatement],
                ReadPrivilegesAsync,
                cancellationToken
            )
            .ConfigureAwait(false);

        var usageDenied = usage.Contains(false);
        if (!usageDenied && denied.IsEmpty)
        {
            return;
        }

        var message = usageDenied
            ? $"The provider's role lacks USAGE on schema '{name}'."
            : $"The provider's role lacks privileges on {string.Join(", ", denied)} in schema '{name}': it needs SELECT on schema_version and SELECT, INSERT, UPDATE and DELETE on every other table.";
        throw new CoordinationException(
            CoordinationErrorCodes.AccessDenied,
            outcomeUnknown: false,
            message
        );
    }
}
