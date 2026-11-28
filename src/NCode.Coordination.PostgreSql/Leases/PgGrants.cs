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
    /// Gets the instant a new grant expires.
    /// </summary>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="terms">The terms of the grant.</param>
    /// <returns>The deadline, or null when the grant is bound to a session and lives as it does.</returns>
    public static DateTime? DeadlineOf(DateTime now, LeaseTerms terms)
    {
        if (terms.IsBoundToSession)
        {
            return null;
        }

        return PgCall.DeadlineAfter(now, terms.TimeToLive);
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
        var session = PgColumns.ReadNullableText(reader, offset + 4);
        var deadline = PgColumns.ReadNullableUtc(reader, offset + 5);
        var value = PgColumns.ReadNullableBytes(reader, offset + 6);
        var sessionDeadline = PgColumns.ReadNullableUtc(reader, offset + 7);

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
        var now = PgColumns.ReadUtc(reader, 0);
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
        return PgColumns.ReadUtc(reader, 0);
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
            .ReadAsync(reader => PgColumns.ReadUtc(reader, 0), cancellationToken)
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

        var existing = ImmutableArray.CreateBuilder<PgGrantRow>(rows.Length);

        foreach (var row in rows)
        {
            if (row.Row is { } grant)
            {
                existing.Add(grant);
            }
        }

        var grants = existing.ToImmutable();

        return new PgGrantRead(rows[0].Now, grants);
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
    private static ImmutableArray<PgGrantRow> Live(PgGrantRead read)
    {
        var live = ImmutableArray.CreateBuilder<PgGrantRow>(read.Rows.Length);

        foreach (var row in read.Rows)
        {
            if (row.StateAt(read.Now) == PgRecordState.Live)
            {
                live.Add(row);
            }
        }

        return live.ToImmutable();
    }

    /// <summary>
    /// Tells whether a read met a grant whose own deadline or whose session's deadline has passed.
    /// </summary>
    /// <param name="read">What the read returned.</param>
    /// <returns><see langword="true"/> when a grant of the read is expired or its session is.</returns>
    private static bool HoldsExpired(PgGrantRead read)
    {
        foreach (var row in read.Rows)
        {
            if (IsExpired(row, read.Now))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the grant row of a held grant when the grant is still the live one: the slot, the fencing token and the
    /// owner token all match.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <returns>The row, or null when the grant has ended.</returns>
    private static PgGrantRow? FindHeld(PgGrantStart start, PgHeldGrant held)
    {
        var now = start.Change.Now;

        foreach (var row in start.Rows)
        {
            if (
                row.Slot == held.Slot
                && row.Fencing == held.Fencing
                && string.Equals(row.Owner, held.Owner, StringComparison.Ordinal)
                && row.StateAt(now) == PgRecordState.Live
            )
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the record table, whose keyspaces' ids, transactions and changes the grants share.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the statement that reads a resource's grant rows.
    /// </summary>
    private string SelectGrantsSql { get; } = table.Apply(SelectGrants);

    /// <summary>
    /// Gets the statement that reads the last fencing token of a resource.
    /// </summary>
    private string SelectLastSql { get; } = table.Apply(SelectLast);

    /// <summary>
    /// Gets the statement that reads the deadline of a session's row.
    /// </summary>
    private string SelectSessionSql { get; } = table.Apply(SelectSession);

    /// <summary>
    /// Gets the statement that reads the server's instant and a resource's grant rows.
    /// </summary>
    private string SelectNowSql { get; } = table.Apply(SelectNow);

    /// <summary>
    /// Gets the statement that writes a grant.
    /// </summary>
    private string UpsertGrantSql { get; } = table.Apply(UpsertGrant);

    /// <summary>
    /// Gets the statement that stores the last fencing token of a resource.
    /// </summary>
    private string UpsertFencingSql { get; } = table.Apply(UpsertFencing);

    /// <summary>
    /// Gets the statement that moves the deadline of a grant.
    /// </summary>
    private string RenewGrantSql { get; } = table.Apply(RenewGrant);

    /// <summary>
    /// Gets the statement that deletes a grant.
    /// </summary>
    private string DeleteGrantSql { get; } = table.Apply(DeleteGrant);

    /// <summary>
    /// Runs an operation with a keyspace's id; when the operation finds the keyspace's row missing, resolves the id
    /// again and runs it once more.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="operation"/> returns.</typeparam>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="operation">The operation, which takes the keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="operation"/> returned.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the operation is null.</exception>
    /// <exception cref="CoordinationException">
    /// The server failed, or the row was missing on both runs, which did not apply.
    /// </exception>
    public ValueTask<T> RunInKeyspaceAsync<T>(
        Keyspace keyspace,
        Func<long, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken
    )
    {
        return Table.RunInKeyspaceAsync(keyspace, operation, cancellationToken);
    }

    /// <summary>
    /// Runs an operation with a keyspace's id; when the operation finds the keyspace's row missing, resolves the id
    /// again and runs it once more.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="operation">The operation, which takes the keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the operation has.</returns>
    /// <exception cref="ArgumentNullException">The keyspace or the operation is null.</exception>
    /// <exception cref="CoordinationException">
    /// The server failed, or the row was missing on both runs, which did not apply.
    /// </exception>
    public ValueTask RunInKeyspaceAsync(
        Keyspace keyspace,
        Func<long, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        return Table.RunInKeyspaceAsync(keyspace, operation, cancellationToken);
    }

    /// <summary>
    /// Runs a short transaction on one connection.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="body"/> returns.</typeparam>
    /// <param name="isolation">The transaction's isolation.</param>
    /// <param name="body">Runs the transaction's batches, and completes it.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="body"/> returned.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    /// <exception cref="PgKeyspaceMissingException">A statement found its keyspace's row missing.</exception>
    public ValueTask<T> TransactAsync<T>(
        PgIsolation isolation,
        Func<PgTransaction, CancellationToken, ValueTask<T>> body,
        CancellationToken cancellationToken
    )
    {
        return Table.TransactAsync(isolation, body, cancellationToken);
    }

    /// <summary>
    /// Commits a change in its transaction, or rolls the transaction back when the change took no revision.
    /// </summary>
    /// <param name="transaction">The transaction that <see cref="BeginAsync"/> began.</param>
    /// <param name="change">The change.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the transaction has ended.</returns>
    /// <exception cref="ArgumentNullException">The transaction or the change is null.</exception>
    public ValueTask CommitAsync(
        PgTransaction transaction,
        PgKeyspaceChange change,
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        return Table.CommitAsync(transaction, change, keyspaceId, cancellationToken);
    }

    /// <summary>
    /// Begins a change to a resource's grants in a transaction: its first batch locks the keyspace's row and reads its
    /// revision, reads the server's instant, the resource's grant rows with the deadlines of their sessions, the last
    /// fencing token of the resource and the row of the terms' session.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="scope">The keyspace, kind and resource whose grants the change reads.</param>
    /// <param name="session">The id of the session that the terms bind to, or null when they bind to none.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The change and what the batch read.</returns>
    /// <exception cref="ArgumentNullException">The transaction or the scope is null.</exception>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    public async ValueTask<PgGrantStart> BeginAsync(
        PgTransaction transaction,
        PgGrantScope scope,
        string? session,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(scope);

        var keyspaceId = scope.KeyspaceId;
        var kind = scope.Kind;
        var index = PgKeys.Index(scope.Resource);
        var depth = (short)scope.Resource.PartitionDepth;
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

        var now = read.Clocks[0];
        var origin = new PgChangeOrigin(keyspaceId, read.Revisions[0], now);
        var change = Table.NewChange(origin);
        var last = read.Lasts.Length == 0 ? 0 : read.Lasts[0];
        DateTime? sessionDeadline = read.Deadlines.Length == 0 ? null : read.Deadlines[0];

        return new PgGrantStart(change, read.Rows, last, sessionDeadline);
    }

    /// <summary>
    /// Gives a resource's slot a new holder: writes the grant's row and the resource's fencing row in the change.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="grant">The grant to write.</param>
    /// <returns>The fencing token of the grant, which is the one after the last the resource was given.</returns>
    /// <exception cref="ArgumentNullException">The start or the grant is null.</exception>
    public long Grant(PgGrantStart start, long keyspaceId, PgNewGrant grant)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(grant);

        var fencing = start.Last + 1;
        var upsert = Upsert(keyspaceId, grant, fencing);
        var fence = Fence(keyspaceId, grant, fencing);
        start.Change.Write(upsert);
        start.Change.Write(fence);

        return fencing;
    }

    /// <summary>
    /// Moves the deadline of a grant when it is still the live one: the slot, the fencing token and the owner token all
    /// match.
    /// </summary>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="timeToLive">The time to live that the grant is renewed for.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the grant was renewed; <see langword="false"/> when it has ended.</returns>
    /// <exception cref="ArgumentNullException">The grant or the time to live is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<bool> RenewHeldAsync(
        PgHeldGrant held,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(timeToLive);

        return await ChangeHeldAsync(
                held,
                (id, now) => RenewStatement(id, held, now, timeToLive),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a grant when it is still the live one: the slot, the fencing token and the owner token all match.
    /// </summary>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the grant was deleted; <see langword="false"/> when it has ended.</returns>
    /// <exception cref="ArgumentNullException">The grant is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<bool> ReleaseHeldAsync(
        PgHeldGrant held,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(held);

        return await ChangeHeldAsync(held, (id, _) => Delete(id, held), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the statement that renews a held grant from the server's instant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <param name="timeToLive">The time to live that the grant is renewed for.</param>
    /// <returns>The statement.</returns>
    private PgStatement RenewStatement(
        long keyspaceId,
        PgHeldGrant held,
        DateTime now,
        TimeToLive timeToLive
    )
    {
        var deadline = PgCall.DeadlineAfter(now, timeToLive);

        return Renew(keyspaceId, held, deadline);
    }

    /// <summary>
    /// Applies a write to a held grant when it is still the live one, in a transaction on the grant's keyspace.
    /// </summary>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="write">Builds the write from the keyspace's id and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<bool> ChangeHeldAsync(
        PgHeldGrant held,
        Func<long, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        return await RunInKeyspaceAsync<bool>(
                held.Resource.Keyspace,
                (id, token) => ChangeHeldInKeyspaceAsync(id, held, write, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Changes a held grant in a keyspace, in a transaction.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="write">Builds the write from the keyspace's id and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    private async ValueTask<bool> ChangeHeldInKeyspaceAsync(
        long keyspaceId,
        PgHeldGrant held,
        Func<long, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        return await TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) =>
                    ChangeHeldInTransactionAsync(transaction, keyspaceId, held, write, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, ends the expired sessions it met, makes the write when the grant is still the live one and
    /// commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="write">Builds the write from the keyspace's id and the server's instant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns><see langword="true"/> when the write was made; <see langword="false"/> when the grant has ended.</returns>
    private async ValueTask<bool> ChangeHeldInTransactionAsync(
        PgTransaction transaction,
        long keyspaceId,
        PgHeldGrant held,
        Func<long, DateTime, PgStatement> write,
        CancellationToken cancellationToken
    )
    {
        var scope = new PgGrantScope(keyspaceId, held.Kind, held.Resource);

        var start = await BeginAsync(transaction, scope, null, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions(null);

        var live = FindHeld(start, held);
        if (live is not null)
        {
            var statement = write(keyspaceId, start.Change.Now);
            start.Change.Write(statement);
        }

        await Table
            .CommitAsync(transaction, start.Change, keyspaceId, cancellationToken)
            .ConfigureAwait(false);
        return live is not null;
    }

    /// <summary>
    /// Builds the statement that writes a grant, replacing the row that holds its slot.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="grant">The grant to write.</param>
    /// <param name="fencing">The fencing token of the grant.</param>
    /// <returns>The statement.</returns>
    private PgStatement Upsert(long keyspaceId, PgNewGrant grant, long fencing)
    {
        var resource = grant.Resource;
        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;
        var keyText = PgKeys.KeyText(resource);
        var sessionArgument = PgArgument.Typed(grant.Session, NpgsqlDbType.Text);
        var deadlineArgument = PgArgument.Typed(grant.Deadline, NpgsqlDbType.TimestampTz);
        var valueArgument = PgArgument.Typed(grant.Value, NpgsqlDbType.Bytea);

        return new PgStatement(
            UpsertGrantSql,
            keyspaceId,
            grant.Kind,
            indexArgument,
            depth,
            keyText,
            grant.Slot,
            grant.Capacity,
            fencing,
            grant.Owner,
            sessionArgument,
            deadlineArgument,
            valueArgument
        );
    }

    /// <summary>
    /// Builds the statement that stores the last fencing token given to a resource.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="grant">The grant that was given.</param>
    /// <param name="fencing">The fencing token that was given.</param>
    /// <returns>The statement.</returns>
    private PgStatement Fence(long keyspaceId, PgNewGrant grant, long fencing)
    {
        var resource = grant.Resource;
        var index = PgKeys.Index(resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)resource.PartitionDepth;
        var keyText = PgKeys.KeyText(resource);

        return new PgStatement(
            UpsertFencingSql,
            keyspaceId,
            grant.Kind,
            indexArgument,
            depth,
            keyText,
            fencing
        );
    }

    /// <summary>
    /// Builds the statement that moves the deadline of a grant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <param name="deadline">The new deadline.</param>
    /// <returns>The statement.</returns>
    private PgStatement Renew(long keyspaceId, PgHeldGrant held, DateTime deadline)
    {
        var index = PgKeys.Index(held.Resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)held.Resource.PartitionDepth;
        var deadlineArgument = PgArgument.Typed(deadline, NpgsqlDbType.TimestampTz);

        return new PgStatement(
            RenewGrantSql,
            keyspaceId,
            held.Kind,
            indexArgument,
            depth,
            held.Slot,
            deadlineArgument,
            held.Fencing
        );
    }

    /// <summary>
    /// Builds the statement that deletes a grant.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="held">The grant the caller holds.</param>
    /// <returns>The statement.</returns>
    private PgStatement Delete(long keyspaceId, PgHeldGrant held)
    {
        var index = PgKeys.Index(held.Resource);
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);
        var depth = (short)held.Resource.PartitionDepth;

        return new PgStatement(
            DeleteGrantSql,
            keyspaceId,
            held.Kind,
            indexArgument,
            depth,
            held.Slot,
            held.Fencing
        );
    }

    /// <summary>
    /// Reads the grants of a resource that are live. A read that meets a grant whose own deadline or whose session's
    /// deadline has passed runs again in a transaction that first waits for the keyspace's writers, so the answer
    /// follows a write that renews or replaces the grant.
    /// </summary>
    /// <param name="scope">The keyspace, kind and resource whose grants are read.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The live grants, by slot; none when the resource is free.</returns>
    /// <exception cref="ArgumentNullException">The scope is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<ImmutableArray<PgGrantRow>> ReadAsync(
        PgGrantScope scope,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);

        var statement = SelectNowStatement(scope);

        var first = await Table
            .ExecuteAsync([statement], ReadRowsAsync, cancellationToken)
            .ConfigureAwait(false);

        if (!HoldsExpired(first))
        {
            return Live(first);
        }

        var second = await Table
            .ReadAfterWritersAsync(scope.KeyspaceId, statement, ReadRowsAsync, cancellationToken)
            .ConfigureAwait(false);

        return Live(second);
    }

    /// <summary>
    /// Builds the statement that reads the server's instant and a resource's grant rows.
    /// </summary>
    /// <param name="scope">The keyspace, kind and resource whose grants are read.</param>
    /// <returns>The statement.</returns>
    private PgStatement SelectNowStatement(PgGrantScope scope)
    {
        var index = PgKeys.Index(scope.Resource);
        var depth = (short)scope.Resource.PartitionDepth;
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);

        return new PgStatement(SelectNowSql, scope.KeyspaceId, scope.Kind, indexArgument, depth);
    }
}
