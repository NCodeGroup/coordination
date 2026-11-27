// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
    /// <summary>The oldest server version number that the provider supports: PostgreSQL 15.</summary>
    private const int MinimumVersion = 150000;

    /// <summary>The statement that reads the server's version, role and durability settings.</summary>
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
        SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relkind IN ('r', 'p') AND ((c.relname = ANY($2::text[]) AND NOT (has_table_privilege(c.oid, 'SELECT') AND has_table_privilege(c.oid, 'INSERT') AND has_table_privilege(c.oid, 'UPDATE') AND has_table_privilege(c.oid, 'DELETE'))) OR (c.relname = 'schema_version' AND NOT has_table_privilege(c.oid, 'SELECT'))) ORDER BY c.relname
        """;

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
        if (new NpgsqlConnectionStringBuilder(source.ConnectionString).Multiplexing)
        {
            throw Unsupported(
                "The provider's data source multiplexes commands, which its listening connection and SET LOCAL can't share; turn Multiplexing off."
            );
        }

        await CheckServerAsync(cancellationToken).ConfigureAwait(false);

        if (settings.ApplyMigrations)
        {
            await PgMigrations
                .ApplyAsync(server, settings.Schema, cancellationToken)
                .ConfigureAwait(false);
        }

        await CheckPrivilegesAsync(cancellationToken).ConfigureAwait(false);
        await CheckVersionAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CoordinationException Unsupported(string message) =>
        new(CoordinationErrorCodes.Unsupported, false, message);

    private async ValueTask CheckServerAsync(CancellationToken cancellationToken)
    {
        var (version, inRecovery, fsync, commit, names) = await server
            .ExecuteAsync(
                [new PgStatement(ServerSql)],
                async (results, token) =>
                {
                    var rows = await results
                        .ReadAsync(
                            reader =>
                                (
                                    reader.GetInt32(0),
                                    reader.GetBoolean(1),
                                    reader.GetString(2),
                                    reader.GetString(3),
                                    reader.GetString(4)
                                ),
                            token
                        )
                        .ConfigureAwait(false);
                    return rows[0];
                },
                cancellationToken
            )
            .ConfigureAwait(false);

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

        if (
            profile.RequiresSynchronousStandby
            && (
                string.IsNullOrWhiteSpace(names)
                || !(
                    string.Equals(commit, "on", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(commit, "remote_apply", StringComparison.OrdinalIgnoreCase)
                )
            )
        )
        {
            throw Unsupported(
                $"The '{profile.Name}' profile needs synchronous_standby_names to name a standby and synchronous_commit on or remote_apply, and the provider's sessions see '{names}' and '{commit}'."
            );
        }
    }

    private async ValueTask CheckVersionAsync(CancellationToken cancellationToken)
    {
        var (version, compatibleFrom) = await PgMigrations
            .ReadVersionAsync(server, settings.Schema, cancellationToken)
            .ConfigureAwait(false);

        var latest = PgMigrations.Latest;
        var name = settings.Schema.Name;

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

    private async ValueTask CheckPrivilegesAsync(CancellationToken cancellationToken)
    {
        var name = settings.Schema.Name;
        var tables = PgMigrations.Tables.ToArray();

        var (usage, denied) = await server
            .ExecuteAsync(
                [
                    new PgStatement(SchemaPrivilegeSql, PgArgument.Of(name)),
                    new PgStatement(TablePrivilegesSql, PgArgument.Of(name), PgArgument.Of(tables)),
                ],
                async (results, token) =>
                {
                    var usageRows = await results
                        .ReadAsync(reader => reader.GetBoolean(0), token)
                        .ConfigureAwait(false);
                    var deniedRows = await results
                        .ReadAsync(reader => reader.GetString(0), token)
                        .ConfigureAwait(false);
                    return (usageRows, deniedRows);
                },
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
