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
    /// <summary>
    /// The template of the statement that reads a resource's grant rows beside the deadlines of their sessions.
    /// </summary>
    private const string SelectGrants = """
        SELECT g.slot, g.capacity, g.fencing, g.owner, g.session_id, g.deadline, g.value, s.deadline
        FROM {schema}.grants g
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = g.keyspace_id AND s.id = g.session_id
        WHERE g.keyspace_id = $1 AND g.kind = $2 AND g.idx = $3 AND g.depth = $4
        ORDER BY g.slot
        """;

    /// <summary>
    /// The template of the statement that reads the last fencing token of a resource.
    /// </summary>
    private const string SelectLast = """
        SELECT last FROM {schema}.fencing WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4
        """;

    /// <summary>
    /// The template of the statement that reads the deadline of a session's row.
    /// </summary>
    private const string SelectSession = """
        SELECT deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = $2
        """;

    /// <summary>
    /// The template of the statement that reads the server's instant and a resource's grant rows.
    /// </summary>
    private const string SelectNow = """
        SELECT now.t, g.slot, g.capacity, g.fencing, g.owner, g.session_id, g.deadline, g.value, s.deadline
        FROM (SELECT clock_timestamp() AS t) now
        LEFT JOIN {schema}.grants g
            ON g.keyspace_id = $1 AND g.kind = $2 AND g.idx = $3 AND g.depth = $4
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = g.keyspace_id AND s.id = g.session_id
        ORDER BY g.slot
        """;

    /// <summary>
    /// The template of the statement that writes a grant, replacing the row that holds its slot.
    /// </summary>
    private const string UpsertGrant = """
        INSERT INTO {schema}.grants
            (keyspace_id, kind, idx, depth, key, slot, capacity, fencing, owner, session_id, deadline, value)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
        ON CONFLICT (keyspace_id, kind, idx, depth, slot) DO UPDATE SET
            key = EXCLUDED.key, capacity = EXCLUDED.capacity, fencing = EXCLUDED.fencing, owner = EXCLUDED.owner,
            session_id = EXCLUDED.session_id, deadline = EXCLUDED.deadline, value = EXCLUDED.value
        """;

    /// <summary>
    /// The template of the statement that stores the last fencing token of a resource.
    /// </summary>
    private const string UpsertFencing = """
        INSERT INTO {schema}.fencing (keyspace_id, kind, idx, depth, key, last)
        VALUES ($1, $2, $3, $4, $5, $6)
        ON CONFLICT (keyspace_id, kind, idx, depth) DO UPDATE SET last = EXCLUDED.last
        """;

    /// <summary>
    /// The template of the statement that moves the deadline of a grant.
    /// </summary>
    private const string RenewGrant = """
        UPDATE {schema}.grants SET deadline = $6
        WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4 AND slot = $5 AND fencing = $7
        """;

    /// <summary>
    /// The template of the statement that deletes a grant.
    /// </summary>
    private const string DeleteGrant = """
        DELETE FROM {schema}.grants
        WHERE keyspace_id = $1 AND kind = $2 AND idx = $3 AND depth = $4 AND slot = $5 AND fencing = $6
        """;

    /// <summary>
    /// Reads a column of an instant as UTC.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The instant, of kind UTC.</returns>
    private static DateTime ReadUtc(NpgsqlDataReader reader, int ordinal)
    {
        var instant = reader.GetDateTime(ordinal);

        return DateTime.SpecifyKind(instant, DateTimeKind.Utc);
    }

    /// <summary>
    /// Reads a column of an instant that may be null, as UTC.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The instant, of kind UTC, or null.</returns>
    private static DateTime? ReadNullableUtc(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return ReadUtc(reader, ordinal);
    }

    /// <summary>
    /// Reads a column of text that may be null.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The text, or null.</returns>
    private static string? ReadNullableText(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetString(ordinal);
    }

    /// <summary>
    /// Reads a column of bytes that may be null.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The bytes, or null.</returns>
    private static byte[]? ReadNullableBytes(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<byte[]>(ordinal);
    }

    /// <summary>
    /// Maps a row of the <c>grants</c> table, which the statements select in the same column order.
    /// </summary>
    /// <param name="reader">The reader, positioned on the row.</param>
    /// <param name="offset">The position of the row's first column.</param>
    /// <returns>The row.</returns>
    private static PgGrantRow MapRow(NpgsqlDataReader reader, int offset)
    {
        var slot = reader.GetInt32(offset);
        var capacity = reader.GetInt32(offset + 1);
        var fencing = reader.GetInt64(offset + 2);
        var owner = reader.GetString(offset + 3);
        var session = ReadNullableText(reader, offset + 4);
        var deadline = ReadNullableUtc(reader, offset + 5);
        var value = ReadNullableBytes(reader, offset + 6);
        var sessionDeadline = ReadNullableUtc(reader, offset + 7);

        return new PgGrantRow(
            slot,
            capacity,
            fencing,
            owner,
            session,
            deadline,
            value,
            sessionDeadline
        );
    }

    /// <summary>
    /// Reads a row of the result of the statement that lists a resource's grant rows beside the server's instant.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The server's instant and the grant row, or null when the resource has none.</returns>
    private static (DateTime Now, PgGrantRow? Row) ReadNowAndRow(NpgsqlDataReader reader)
    {
        var now = ReadUtc(reader, 0);
        var row = reader.IsDBNull(1) ? null : MapRow(reader, 1);

        return (now, row);
    }

    /// <summary>
    /// Reads the deadline of a session's row from the first column.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The deadline, of kind UTC.</returns>
    private static DateTime ReadSessionDeadline(NpgsqlDataReader reader)
    {
        return ReadUtc(reader, 0);
    }

    /// <summary>
    /// Reads the results of the first batch of a change to a resource's grants.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What the batch's five statements returned.</returns>
    private static async ValueTask<PgGrantStartRead> ReadStartAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var revisions = await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
        var clocks = await results
            .ReadAsync(reader => reader.GetDateTime(0), cancellationToken)
            .ConfigureAwait(false);
        var rows = await results
            .ReadAsync(reader => MapRow(reader, 0), cancellationToken)
            .ConfigureAwait(false);
        var lasts = await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
        var deadlines = await results
            .ReadAsync(ReadSessionDeadline, cancellationToken)
            .ConfigureAwait(false);

        return new PgGrantStartRead(revisions, clocks, rows, lasts, deadlines);
    }

    /// <summary>
    /// Reads the result of the statement that lists a resource's grant rows with the server's instant.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The server's instant and the rows that exist.</returns>
    private static async ValueTask<PgGrantRead> ReadRowsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(ReadNowAndRow, cancellationToken).ConfigureAwait(false);

        var existing = rows.Select(row => row.Row).OfType<PgGrantRow>().ToImmutableArray();

        return new PgGrantRead(rows[0].Now, existing);
    }

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
    private static ImmutableArray<PgGrantRow> Live(PgGrantRead read) =>
        [.. read.Rows.Where(row => row.StateAt(read.Now) == PgRecordState.Live)];

    /// <summary>
    /// Gets the record table, whose keyspaces' ids, transactions and changes the grants share.
    /// </summary>
    public PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the statement that reads a resource's grant rows.
    /// </summary>
    private string SelectGrantsSql { get; } = table.Settings.Schema.Apply(SelectGrants);

    /// <summary>
    /// Gets the statement that reads the last fencing token of a resource.
    /// </summary>
    private string SelectLastSql { get; } = table.Settings.Schema.Apply(SelectLast);

    /// <summary>
    /// Gets the statement that reads the deadline of a session's row.
    /// </summary>
    private string SelectSessionSql { get; } = table.Settings.Schema.Apply(SelectSession);

    /// <summary>
    /// Gets the statement that reads the server's instant and a resource's grant rows.
    /// </summary>
    private string SelectNowSql { get; } = table.Settings.Schema.Apply(SelectNow);

    /// <summary>
    /// Gets the statement that writes a grant.
    /// </summary>
    private string UpsertGrantSql { get; } = table.Settings.Schema.Apply(UpsertGrant);

    /// <summary>
    /// Gets the statement that stores the last fencing token of a resource.
    /// </summary>
    private string UpsertFencingSql { get; } = table.Settings.Schema.Apply(UpsertFencing);

    /// <summary>
    /// Gets the statement that moves the deadline of a grant.
    /// </summary>
    private string RenewGrantSql { get; } = table.Settings.Schema.Apply(RenewGrant);

    /// <summary>
    /// Gets the statement that deletes a grant.
    /// </summary>
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
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var sessionArgument = PgArgument.Typed(session, NpgsqlDbType.Text);

        var lockKeyspace = Table.LockKeyspace(keyspaceId);
        var clock = Table.Clock();
        var selectGrants = new PgStatement(SelectGrantsSql, keyspaceId, kind, indexArgument, depth);
        var selectLast = new PgStatement(SelectLastSql, keyspaceId, kind, indexArgument, depth);
        var selectSession = new PgStatement(SelectSessionSql, keyspaceId, sessionArgument);
        PgStatement[] statements = [lockKeyspace, clock, selectGrants, selectLast, selectSession];

        var read = await transaction
            .FirstAsync(statements, ReadStartAsync, cancellationToken)
            .ConfigureAwait(false);

        if (read.Revisions.Length == 0)
        {
            throw new PgKeyspaceMissingException(PgKeyspaceMissingException.RowMissingMessage);
        }

        var now = DateTime.SpecifyKind(read.Clocks[0], DateTimeKind.Utc);
        var change = Table.NewChange(keyspaceId, read.Revisions[0], now);
        var last = read.Lasts.Length == 0 ? 0 : read.Lasts[0];
        DateTime? sessionDeadline = read.Deadlines.Length == 0 ? null : read.Deadlines[0];

        return new PgGrantStart(change, read.Rows, last, sessionDeadline);
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

        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;
        var keyText = PgKeys.KeyText(resource);
        var sessionArgument = PgArgument.Typed(session, NpgsqlDbType.Text);
        var deadlineArgument = PgArgument.Typed(deadline, NpgsqlDbType.TimestampTz);
        var valueArgument = PgArgument.Typed(value, NpgsqlDbType.Bytea);

        return new PgStatement(
            UpsertGrantSql,
            keyspaceId,
            kind,
            indexArgument,
            depth,
            keyText,
            slot,
            capacity,
            fencing,
            owner,
            sessionArgument,
            deadlineArgument,
            valueArgument
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

        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;
        var keyText = PgKeys.KeyText(resource);

        return new PgStatement(
            UpsertFencingSql,
            keyspaceId,
            kind,
            indexArgument,
            depth,
            keyText,
            last
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

        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;
        var deadlineArgument = PgArgument.Typed(deadline, NpgsqlDbType.TimestampTz);

        return new PgStatement(
            RenewGrantSql,
            keyspaceId,
            kind,
            indexArgument,
            depth,
            slot,
            deadlineArgument,
            fencing
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

        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;

        return new PgStatement(
            DeleteGrantSql,
            keyspaceId,
            kind,
            indexArgument,
            depth,
            slot,
            fencing
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

        var statement = SelectNowStatement(keyspaceId, kind, index, depth);

        var first = await Table
            .Server.ExecuteAsync([statement], ReadRowsAsync, cancellationToken)
            .ConfigureAwait(false);

        if (!first.Rows.Any(row => IsExpired(row, first.Now)))
        {
            return Live(first);
        }

        var second = await Table
            .ReadAfterWritersAsync(keyspaceId, statement, ReadRowsAsync, cancellationToken)
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
    private PgStatement SelectNowStatement(long keyspaceId, short kind, byte[] index, short depth)
    {
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);

        return new PgStatement(SelectNowSql, keyspaceId, kind, indexArgument, depth);
    }
}
