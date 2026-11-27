// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The reads and writes that leases, semaphores and elections share: a grant is a row of the <c>grants</c> table of
/// one kind, resource and slot, and a resource's fencing tokens are kept in its row of the <c>fencing</c> table, which
/// outlives its grants. A change locks the keyspace's row but takes no revision and logs no entry.
/// </summary>
/// <param name="table">The record table, whose keyspaces' ids, transactions and changes the grants share.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal sealed class PgGrants(PgRecordTable table)
{
    /// <summary>The template of the statement that reads a resource's grant rows beside the deadlines of their sessions.</summary>
    private const string SelectGrants = """
        SELECT g.slot, g.capacity, g.fencing, g.owner, g.session_id, g.deadline, g.value, s.deadline
        FROM {schema}.grants g
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = g.keyspace_id AND s.id = g.session_id
        WHERE g.keyspace_id = $1 AND g.kind = $2 AND g.idx = $3 AND g.depth = $4
        ORDER BY g.slot
        """;

    /// <summary>The template of the statement that reads the last fencing token of a resource.</summary>
    private const string SelectLast = """
        SELECT last FROM {schema}.fencing WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4
        """;

    /// <summary>The template of the statement that reads the deadline of a session's row.</summary>
    private const string SelectSession = """
        SELECT deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = $2
        """;

    /// <summary>The template of the statement that reads the server's instant and a resource's grant rows.</summary>
    private const string SelectNow = """
        SELECT now.t, g.slot, g.capacity, g.fencing, g.owner, g.session_id, g.deadline, g.value, s.deadline
        FROM (SELECT clock_timestamp() AS t) now
        LEFT JOIN {schema}.grants g
            ON g.keyspace_id = $1 AND g.kind = $2 AND g.idx = $3 AND g.depth = $4
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = g.keyspace_id AND s.id = g.session_id
        ORDER BY g.slot
        """;

    /// <summary>The template of the statement that waits for the writers of a keyspace's row.</summary>
    private const string LockShare = """
        SELECT 1 FROM {schema}.keyspaces WHERE id = $1 FOR SHARE
        """;

    /// <summary>The template of the statement that writes a grant, replacing the row that holds its slot.</summary>
    private const string UpsertGrant = """
        INSERT INTO {schema}.grants
            (keyspace_id, kind, idx, depth, key, slot, capacity, fencing, owner, session_id, deadline, value)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
        ON CONFLICT (keyspace_id, kind, idx, depth, slot) DO UPDATE SET
            key = EXCLUDED.key, capacity = EXCLUDED.capacity, fencing = EXCLUDED.fencing, owner = EXCLUDED.owner,
            session_id = EXCLUDED.session_id, deadline = EXCLUDED.deadline, value = EXCLUDED.value
        """;

    /// <summary>The template of the statement that stores the last fencing token of a resource.</summary>
    private const string UpsertFencing = """
        INSERT INTO {schema}.fencing (keyspace_id, kind, idx, depth, key, last)
        VALUES ($1, $2, $3, $4, $5, $6)
        ON CONFLICT (keyspace_id, kind, idx, depth) DO UPDATE SET last = EXCLUDED.last
        """;

    /// <summary>The template of the statement that moves the deadline of a grant.</summary>
    private const string RenewGrant = """
        UPDATE {schema}.grants SET deadline = $6
        WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4 AND slot = $5 AND fencing = $7
        """;

    /// <summary>The template of the statement that deletes a grant.</summary>
    private const string DeleteGrant = """
        DELETE FROM {schema}.grants
        WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4 AND slot = $5 AND fencing = $6
        """;

    /// <summary>
    /// Gets the record table, whose keyspaces' ids, transactions and changes the grants share.
    /// </summary>
    public PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>Gets the statement that reads a resource's grant rows.</summary>
    private string SelectGrantsSql { get; } = table.Settings.Schema.Apply(SelectGrants);

    /// <summary>Gets the statement that reads the last fencing token of a resource.</summary>
    private string SelectLastSql { get; } = table.Settings.Schema.Apply(SelectLast);

    /// <summary>Gets the statement that reads the deadline of a session's row.</summary>
    private string SelectSessionSql { get; } = table.Settings.Schema.Apply(SelectSession);

    /// <summary>Gets the statement that reads the server's instant and a resource's grant rows.</summary>
    private string SelectNowSql { get; } = table.Settings.Schema.Apply(SelectNow);

    /// <summary>Gets the statement that waits for the writers of a keyspace's row.</summary>
    private string LockShareSql { get; } = table.Settings.Schema.Apply(LockShare);

    /// <summary>Gets the statement that writes a grant.</summary>
    private string UpsertGrantSql { get; } = table.Settings.Schema.Apply(UpsertGrant);

    /// <summary>Gets the statement that stores the last fencing token of a resource.</summary>
    private string UpsertFencingSql { get; } = table.Settings.Schema.Apply(UpsertFencing);

    /// <summary>Gets the statement that moves the deadline of a grant.</summary>
    private string RenewGrantSql { get; } = table.Settings.Schema.Apply(RenewGrant);

    /// <summary>Gets the statement that deletes a grant.</summary>
    private string DeleteGrantSql { get; } = table.Settings.Schema.Apply(DeleteGrant);

    /// <summary>
    /// Begins a change to a resource's grants in a transaction: its first batch locks the keyspace's row and reads its
    /// revision, reads the server's instant, the resource's grant rows with the deadlines of their sessions, the last
    /// fencing token of the resource and the row of the terms' session.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grants: 1 for leases, 2 for semaphores, 3 for elections.</param>
    /// <param name="resource">The resource that the grants guard.</param>
    /// <param name="session">The id of the session that the terms bind to, or null when they bind to none.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The change and what the batch read.</returns>
    /// <exception cref="ArgumentNullException">The transaction or the resource is null.</exception>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    public async ValueTask<PgGrantStart> BeginAsync(
        PgTransaction transaction,
        long keyspaceId,
        short kind,
        CoordinationKey resource,
        string? session,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(resource);

        var index = PgKeys.Index(resource);
        var depth = (short)resource.PartitionDepth;

        var (revisions, clocks, rows, lasts, deadlines) = await transaction
            .FirstAsync(
                [
                    Table.LockKeyspace(keyspaceId),
                    Table.Clock(),
                    new PgStatement(
                        SelectGrantsSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Of(kind),
                        PgArgument.Typed(index, NpgsqlDbType.Bytea),
                        PgArgument.Of(depth)
                    ),
                    new PgStatement(
                        SelectLastSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Of(kind),
                        PgArgument.Typed(index, NpgsqlDbType.Bytea),
                        PgArgument.Of(depth)
                    ),
                    new PgStatement(
                        SelectSessionSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Typed(session, NpgsqlDbType.Text)
                    ),
                ],
                async (results, token) =>
                {
                    var revisionRows = await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false);
                    var clockRows = await results
                        .ReadAsync(reader => reader.GetDateTime(0), token)
                        .ConfigureAwait(false);
                    var grantRows = await results
                        .ReadAsync(reader => MapRow(reader, 0), token)
                        .ConfigureAwait(false);
                    var lastRows = await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false);
                    var sessionRows = await results
                        .ReadAsync(
                            reader => DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                            token
                        )
                        .ConfigureAwait(false);

                    return (revisionRows, clockRows, grantRows, lastRows, sessionRows);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revisions.Length == 0)
        {
            throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
        }

        var now = DateTime.SpecifyKind(clocks[0], DateTimeKind.Utc);
        var change = Table.NewChange(keyspaceId, revisions[0], now);

        return new PgGrantStart(
            change,
            rows,
            lasts.Length == 0 ? 0 : lasts[0],
            deadlines.Length == 0 ? null : deadlines[0]
        );
    }

    /// <summary>
    /// Builds the statement that writes a grant, replacing the row that holds its slot.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grant.</param>
    /// <param name="resource">The resource that the grant guards.</param>
    /// <param name="slot">The slot the grant holds.</param>
    /// <param name="capacity">The capacity of the semaphore the grant belongs to; 1 for a lease or an election.</param>
    /// <param name="fencing">The fencing token of the grant.</param>
    /// <param name="owner">The owner token of the grant.</param>
    /// <param name="session">The id of the session the grant is bound to, or null.</param>
    /// <param name="deadline">The instant the grant expires, or null when it is bound to a session.</param>
    /// <param name="value">The leader's value of an election, or null.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentNullException">The resource or the owner is null.</exception>
    public PgStatement Upsert(
        long keyspaceId,
        short kind,
        CoordinationKey resource,
        int slot,
        int capacity,
        long fencing,
        string owner,
        string? session,
        DateTime? deadline,
        byte[]? value
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(owner);

        return new PgStatement(
            UpsertGrantSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Of(kind),
            PgArgument.Typed(PgKeys.Index(resource), NpgsqlDbType.Bytea),
            PgArgument.Of((short)resource.PartitionDepth),
            PgArgument.Of(PgKeys.KeyText(resource)),
            PgArgument.Of(slot),
            PgArgument.Of(capacity),
            PgArgument.Of(fencing),
            PgArgument.Of(owner),
            PgArgument.Typed(session, NpgsqlDbType.Text),
            PgArgument.Typed(deadline, NpgsqlDbType.TimestampTz),
            PgArgument.Typed(value, NpgsqlDbType.Bytea)
        );
    }

    /// <summary>
    /// Builds the statement that stores the last fencing token given to a resource.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grants.</param>
    /// <param name="resource">The resource that the grants guard.</param>
    /// <param name="last">The fencing token that was given.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentNullException">The resource is null.</exception>
    public PgStatement Fence(long keyspaceId, short kind, CoordinationKey resource, long last)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new PgStatement(
            UpsertFencingSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Of(kind),
            PgArgument.Typed(PgKeys.Index(resource), NpgsqlDbType.Bytea),
            PgArgument.Of((short)resource.PartitionDepth),
            PgArgument.Of(PgKeys.KeyText(resource)),
            PgArgument.Of(last)
        );
    }

    /// <summary>
    /// Builds the statement that moves the deadline of a grant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grant.</param>
    /// <param name="resource">The resource that the grant guards.</param>
    /// <param name="slot">The slot the grant holds.</param>
    /// <param name="fencing">The fencing token of the grant.</param>
    /// <param name="deadline">The new deadline.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentNullException">The resource is null.</exception>
    public PgStatement Renew(
        long keyspaceId,
        short kind,
        CoordinationKey resource,
        int slot,
        long fencing,
        DateTime deadline
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new PgStatement(
            RenewGrantSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Of(kind),
            PgArgument.Typed(PgKeys.Index(resource), NpgsqlDbType.Bytea),
            PgArgument.Of((short)resource.PartitionDepth),
            PgArgument.Of(slot),
            PgArgument.Typed(deadline, NpgsqlDbType.TimestampTz),
            PgArgument.Of(fencing)
        );
    }

    /// <summary>
    /// Builds the statement that deletes a grant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grant.</param>
    /// <param name="resource">The resource that the grant guards.</param>
    /// <param name="slot">The slot the grant holds.</param>
    /// <param name="fencing">The fencing token of the grant.</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentNullException">The resource is null.</exception>
    public PgStatement Delete(
        long keyspaceId,
        short kind,
        CoordinationKey resource,
        int slot,
        long fencing
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new PgStatement(
            DeleteGrantSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Of(kind),
            PgArgument.Typed(PgKeys.Index(resource), NpgsqlDbType.Bytea),
            PgArgument.Of((short)resource.PartitionDepth),
            PgArgument.Of(slot),
            PgArgument.Of(fencing)
        );
    }

    /// <summary>
    /// Reads the grants of a resource that are live. A read that meets a grant whose own deadline or whose session's
    /// deadline has passed runs again in a transaction that first waits for the keyspace's writers, so the answer
    /// follows a write that renews or replaces the grant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grants.</param>
    /// <param name="resource">The resource that the grants guard.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The live grants, by slot; none when the resource is free.</returns>
    /// <exception cref="ArgumentNullException">The resource is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<ImmutableArray<PgGrantRow>> ReadAsync(
        long keyspaceId,
        short kind,
        CoordinationKey resource,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        var index = PgKeys.Index(resource);
        var depth = (short)resource.PartitionDepth;

        var first = await Table
            .Server.ExecuteAsync(
                [SelectNowStatement(keyspaceId, kind, index, depth)],
                ReadRowsAsync,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!first.Rows.Any(row => IsExpired(row, first.Now)))
        {
            return Live(first);
        }

        var second = await Table
            .Server.TransactAsync(
                PgIsolation.ReadCommitted,
                async (transaction, transactionToken) =>
                    await transaction
                        .FirstAsync(
                            [
                                new PgStatement(LockShareSql, PgArgument.Of(keyspaceId)),
                                SelectNowStatement(keyspaceId, kind, index, depth),
                            ],
                            async (results, readToken) =>
                            {
                                await results
                                    .ReadAsync(reader => reader.GetInt32(0), readToken)
                                    .ConfigureAwait(false);
                                return await ReadRowsAsync(results, readToken)
                                    .ConfigureAwait(false);
                            },
                            transactionToken
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        return Live(second);
    }

    /// <summary>
    /// Builds the statement that reads the server's instant and a resource's grant rows.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="kind">The kind of the grants.</param>
    /// <param name="index">The resource's index value.</param>
    /// <param name="depth">The resource's partition depth.</param>
    /// <returns>The statement.</returns>
    private PgStatement SelectNowStatement(
        long keyspaceId,
        short kind,
        byte[] index,
        short depth
    ) =>
        new(
            SelectNowSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Of(kind),
            PgArgument.Typed(index, NpgsqlDbType.Bytea),
            PgArgument.Of(depth)
        );

    /// <summary>
    /// Reads the result of the statement that lists a resource's grant rows with the server's instant.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The server's instant and the rows that exist.</returns>
    private static async ValueTask<GrantRead> ReadRowsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results
            .ReadAsync(
                reader =>
                    (
                        Now: DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                        Row: reader.IsDBNull(1) ? null : MapRow(reader, 1)
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new GrantRead(rows[0].Now, [.. rows.Select(row => row.Row).OfType<PgGrantRow>()]);
    }

    /// <summary>
    /// Maps a row of the <c>grants</c> table, which the statements select in the same column order.
    /// </summary>
    /// <param name="reader">The reader, positioned on the row.</param>
    /// <param name="offset">The position of the row's first column.</param>
    /// <returns>The row.</returns>
    private static PgGrantRow MapRow(NpgsqlDataReader reader, int offset) =>
        new(
            reader.GetInt32(offset),
            reader.GetInt32(offset + 1),
            reader.GetInt64(offset + 2),
            reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            reader.IsDBNull(offset + 5)
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime(offset + 5), DateTimeKind.Utc),
            reader.IsDBNull(offset + 6) ? null : reader.GetFieldValue<byte[]>(offset + 6),
            reader.IsDBNull(offset + 7)
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime(offset + 7), DateTimeKind.Utc)
        );

    /// <summary>
    /// Tells whether a grant's own deadline or its session's deadline has passed, so a writer may be about to renew or
    /// replace it. A grant whose session's row is gone has ended for good.
    /// </summary>
    /// <param name="row">The grant.</param>
    /// <param name="now">The instant of the read.</param>
    /// <returns><see langword="true"/> when the grant is expired or its session is.</returns>
    private static bool IsExpired(PgGrantRow row, DateTime now) =>
        row.StateAt(now) is PgRecordState.Expired or PgRecordState.SessionExpired;

    /// <summary>
    /// Keeps the grants of a read that are live at its instant.
    /// </summary>
    /// <param name="read">What the read returned.</param>
    /// <returns>The live grants.</returns>
    private static ImmutableArray<PgGrantRow> Live(GrantRead read) =>
        [.. read.Rows.Where(row => row.StateAt(read.Now) == PgRecordState.Live)];

    /// <summary>
    /// Holds what a read of a resource's grants returned.
    /// </summary>
    /// <param name="Now">The server's instant.</param>
    /// <param name="Rows">The grant rows that exist, expired or not.</param>
    private sealed record GrantRead(DateTime Now, ImmutableArray<PgGrantRow> Rows);
}
