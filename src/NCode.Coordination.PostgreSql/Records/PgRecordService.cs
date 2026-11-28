// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The records of the PostgreSQL provider: each write is a short transaction that locks the keyspace's row, decides
/// from the rows it read and commits the writes with the change log.
/// </summary>
/// <param name="table">The record table that the operations share.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal sealed class PgRecordService(PgRecordTable table) : IRecordService
{
    /// <summary>
    /// The template of the statement that lists the rows under a prefix after a position.
    /// </summary>
    private const string ListRows = """
        WITH now AS (SELECT clock_timestamp() AS t)
        SELECT k.revision, now.t, r.idx, r.depth, r.key, r.value, r.revision, r.deadline, r.session_id,
          r.session_deadline
        FROM now CROSS JOIN {schema}.keyspaces k
        LEFT JOIN LATERAL (SELECT x.*, s.deadline AS session_deadline FROM {schema}.records x
          LEFT JOIN {schema}.sessions s ON s.keyspace_id = x.keyspace_id AND s.id = x.session_id
          WHERE x.keyspace_id = k.id AND x.idx >= $2
          AND ($3::bytea IS NULL OR x.idx < $3) AND (x.idx, x.depth) > ($4, $5)
          ORDER BY x.idx, x.depth LIMIT $6) r ON true
        WHERE k.id = $1
        """;

    /// <summary>
    /// Checks that a value is no longer than the largest value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="paramName">The name of the parameter that holds the value.</param>
    /// <exception cref="ArgumentException">The value is longer than <see cref="CoordinationLimits.MaxValueLength"/> bytes.</exception>
    private static void CheckValue(ReadOnlyMemory<byte> value, string paramName)
    {
        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                paramName
            );
        }
    }

    /// <summary>
    /// Gets the id of the session that a lifetime binds a record to, when the session lives in the record's keyspace.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The session's id, or null when the lifetime binds to no session or to one of another keyspace, which
    /// the keyspace's rows can't hold.</returns>
    private static string? SessionOf(CoordinationKey key, RecordLifetime lifetime) =>
        lifetime.Session is { } session && session.Keyspace == key.Keyspace ? session.Value : null;

    /// <summary>
    /// Tells whether a lifetime binds a record to a session that has ended or was never opened in the keyspace.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="ended">The sessions that the transaction ended because they had expired.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <param name="session">The id that <see cref="SessionOf"/> returned for the lifetime.</param>
    /// <returns>True when the lifetime binds to a session that is not live.</returns>
    private static bool BindsToEndedSession(
        PgChangeStart start,
        ImmutableHashSet<string> ended,
        RecordLifetime lifetime,
        string? session
    ) =>
        lifetime.Session is not null
        && (session is null || ended.Contains(session) || !start.Sessions.ContainsKey(session));

    /// <summary>
    /// Gets the session that a row that is about to be replaced was bound to, when that session has ended.
    /// </summary>
    /// <param name="row">The row, or null when the key has none.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <returns>The ended session's id, or null.</returns>
    private static string? ReplacedSession(PgRecordRow? row, DateTime now) =>
        row?.StateAt(now) is PgRecordState.SessionEnded or PgRecordState.SessionExpired
            ? row.Session
            : null;

    /// <summary>
    /// Gets the row of a key when it is live, and logs the removal of the row when it has expired.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="row">The key's row, or null when the key has none.</param>
    /// <returns>The row when it is live; otherwise, null.</returns>
    private static PgRecordRow? TakeLiveRow(PgKeyspaceChange change, PgRecordRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var state = row.StateAt(change.Now);
        if (state == PgRecordState.Expired)
        {
            change.Expire([row]);
        }

        return state == PgRecordState.Live ? row : null;
    }

    /// <summary>
    /// Gets the instant a record written now expires.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="lifetime">The record's lifetime.</param>
    /// <returns>The deadline, or null when the lifetime has no time to live.</returns>
    private static DateTime? DeadlineOf(PgKeyspaceChange change, RecordLifetime lifetime)
    {
        if (lifetime.TimeToLive is not { } timeToLive)
        {
            return null;
        }

        return PgCall.DeadlineAfter(change.Now, timeToLive);
    }

    /// <summary>
    /// Takes a revision and writes a record in it.
    /// </summary>
    /// <param name="request">What the write needs.</param>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="replacedSession">The id of the ended session whose row the write replaces, or null.</param>
    /// <returns>The result that says the record was written, with its revision.</returns>
    private static RecordWriteResult WriteRecord(
        PgPutRequest request,
        PgKeyspaceChange change,
        string? replacedSession
    )
    {
        var revision = change.Begin();
        var deadline = DeadlineOf(change, request.Lifetime);
        change.Put(request.Key, request.Value, deadline, request.Session, replacedSession);

        var written = new Revision(revision);
        return RecordWriteResult.Written(written);
    }

    /// <summary>
    /// Decides a create from the rows that were read.
    /// </summary>
    /// <param name="request">What the write needs.</param>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="row">The key's row, or null when the key has none.</param>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="bindsToEndedSession">Whether the lifetime binds to a session that is not live.</param>
    /// <returns>The result of the create, having made its writes in the change.</returns>
    private static RecordWriteResult DecideCreate(
        PgPutRequest request,
        PgKeyspaceChange change,
        PgRecordRow? row,
        PgRecordRow? liveRow,
        bool bindsToEndedSession
    )
    {
        if (bindsToEndedSession)
        {
            return RecordWriteResult.SessionEnded;
        }

        if (liveRow is not null)
        {
            return RecordWriteResult.Conflict;
        }

        var replacedSession = ReplacedSession(row, change.Now);
        return WriteRecord(request, change, replacedSession);
    }

    /// <summary>
    /// Decides a compare and swap from the rows that were read.
    /// </summary>
    /// <param name="request">What the write needs.</param>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="bindsToEndedSession">Whether the lifetime binds to a session that is not live.</param>
    /// <returns>The result of the swap, having made its writes in the change.</returns>
    private static RecordWriteResult DecideSwap(
        PgPutRequest request,
        PgKeyspaceChange change,
        PgRecordRow? liveRow,
        long expectedRevision,
        bool bindsToEndedSession
    )
    {
        if (bindsToEndedSession)
        {
            return RecordWriteResult.SessionEnded;
        }

        if (liveRow is null)
        {
            return RecordWriteResult.NotFound;
        }

        if (liveRow.Revision != expectedRevision)
        {
            return RecordWriteResult.Conflict;
        }

        return WriteRecord(request, change, null);
    }

    /// <summary>
    /// Decides a compare and delete from the rows that were read.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <returns>The status of the delete, having made its writes in the change.</returns>
    private static RecordDeleteStatus DecideDelete(
        PgKeyspaceChange change,
        PgRecordRow? liveRow,
        long expectedRevision
    )
    {
        if (liveRow is null)
        {
            return RecordDeleteStatus.NotFound;
        }

        if (liveRow.Revision != expectedRevision)
        {
            return RecordDeleteStatus.Conflict;
        }

        change.Begin();
        change.Delete(liveRow);
        return RecordDeleteStatus.Deleted;
    }

    /// <summary>
    /// Takes a live record in the change: deletes its row and returns the record.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <returns>The record that was taken, or null when the key has no live row.</returns>
    private static StoredRecord? DecideTake(
        PgKeyspaceChange change,
        CoordinationKey key,
        PgRecordRow? liveRow
    )
    {
        if (liveRow is null)
        {
            return null;
        }

        change.Begin();
        change.Delete(liveRow);

        var revision = new Revision(liveRow.Revision);
        return new StoredRecord(key, liveRow.Value, revision);
    }

    /// <summary>
    /// Finds where a page of a list starts.
    /// </summary>
    /// <param name="page">The request for the page.</param>
    /// <param name="prefixIndex">The prefix's index value.</param>
    /// <returns>The index value and the depth after which the page starts.</returns>
    /// <exception cref="ArgumentException">The continuation token was not issued for this prefix.</exception>
    private static (byte[] Index, short Depth) ResolveStart(
        RecordPageRequest page,
        byte[] prefixIndex
    )
    {
        if (page.ContinuationToken is null)
        {
            return (prefixIndex, -1);
        }

        if (
            PgKeys.TryReadPageToken(
                page.ContinuationToken,
                prefixIndex,
                out var tokenIndex,
                out var tokenDepth
            )
        )
        {
            return (tokenIndex, (short)tokenDepth);
        }

        throw new ArgumentException(
            "The continuation token was not issued by the PostgreSQL provider for this prefix.",
            nameof(page)
        );
    }

    /// <summary>
    /// Reads one row of the result of <see cref="ListRows"/>.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The keyspace's revision, the server's instant and the record's row, if any.</returns>
    private static PgListItem ReadListItem(NpgsqlDataReader reader)
    {
        var keyspaceRevision = reader.GetInt64(0);
        var instant = reader.GetDateTime(1);
        var now = DateTime.SpecifyKind(instant, DateTimeKind.Utc);
        var row = reader.IsDBNull(2) ? null : PgRecordTable.ReadRowAt(reader, 2);

        return new PgListItem(keyspaceRevision, now, row);
    }

    /// <summary>
    /// Reads the result of <see cref="ListRows"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>One item for each row: the keyspace's revision, the server's instant and the record's row, if any.</returns>
    private static async ValueTask<ImmutableArray<PgListItem>> ReadListAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadListItem, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells whether the records that a page holds include one that has expired.
    /// </summary>
    /// <param name="items">What the list statement returned.</param>
    /// <param name="limit">The most records the page holds.</param>
    /// <returns>True when a row of the page is expired, or bound to a session that has expired.</returns>
    private static bool HoldsExpiredRow(ImmutableArray<PgListItem> items, int limit)
    {
        var page = items.Where(item => item.Row is not null).Take(limit);
        return page.Any(item =>
            item.StateAt() is PgRecordState.Expired or PgRecordState.SessionExpired
        );
    }

    /// <summary>
    /// Lists the live records of a page.
    /// </summary>
    /// <param name="request">The request for the page.</param>
    /// <param name="listed">The items that have a row, in order.</param>
    /// <returns>The live records among the first items that the page holds.</returns>
    private static List<StoredRecord> ReadLiveRecords(PgListRequest request, PgListItem[] listed)
    {
        var records = new List<StoredRecord>();
        foreach (var item in listed.Take(request.Limit))
        {
            if (item.Row is { } row && item.StateAt() == PgRecordState.Live)
            {
                var key = PgRecordTable.DecodeKey(request.Prefix.Keyspace, row.Key, row.Depth);
                var revision = new Revision(row.Revision);
                var record = new StoredRecord(key, row.Value, revision);
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>
    /// Gets the token that continues a list after the last record of a page.
    /// </summary>
    /// <param name="listed">The items that have a row, in order.</param>
    /// <param name="limit">The most records the page holds.</param>
    /// <returns>The token, or null when no row follows the page.</returns>
    private static string? NextPageToken(PgListItem[] listed, int limit)
    {
        if (listed.Length > limit && listed[limit - 1].Row is { } last)
        {
            return PgKeys.PageToken(last.Index, last.Depth);
        }

        return null;
    }

    /// <summary>
    /// Builds a page from what the list statement returned.
    /// </summary>
    /// <param name="request">The request for the page.</param>
    /// <param name="read">What the list statement returned, which has at least one item.</param>
    /// <returns>The page.</returns>
    private static RecordPage ToPage(PgListRequest request, ImmutableArray<PgListItem> read)
    {
        var listed = read.Where(item => item.Row is not null).ToArray();
        var records = ReadLiveRecords(request, listed);
        var next = NextPageToken(listed, request.Limit);
        var revision = new Revision(read[0].KeyspaceRevision);

        return new RecordPage(revision, records) { ContinuationToken = next };
    }

    /// <summary>
    /// Gets the record table that the operations share.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the statement that lists the rows under a prefix after a position.
    /// </summary>
    private string ListSql { get; } = table.Settings.Schema.Apply(ListRows);

    /// <inheritdoc />
    public async ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);
        CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);

        var copy = value.ToArray();
        var session = SessionOf(key, lifetime);
        var request = new PgPutRequest(key, copy, lifetime, session);

        return await Table
            .Keyspaces.RunAsync<RecordWriteResult>(
                key.Keyspace,
                (id, token) => CreateInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a record in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the write needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the create.</returns>
    private async ValueTask<RecordWriteResult> CreateInKeyspaceAsync(
        long id,
        PgPutRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => CreateInTransactionAsync(transaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides a create from the key's row and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the write needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the create.</returns>
    private async ValueTask<RecordWriteResult> CreateInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgPutRequest request,
        CancellationToken cancellationToken
    )
    {
        var start = await Table
            .BeginAsync(transaction, id, [request.Key], request.Sessions, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        var ended = start.EndExpiredSessions();
        start.Rows.TryGetValue(request.Key, out var row);
        var liveRow = TakeLiveRow(change, row);
        var bindsToEndedSession = BindsToEndedSession(
            start,
            ended,
            request.Lifetime,
            request.Session
        );

        var result = DecideCreate(request, change, row, liveRow, bindsToEndedSession);

        await Table.CommitAsync(transaction, change, id, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await Table.ReadOneAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(lifetime);
        CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);

        var copy = value.ToArray();
        var expectedRevision = expected.Value;
        var session = SessionOf(key, lifetime);
        var request = new PgPutRequest(key, copy, lifetime, session);

        return await Table
            .Keyspaces.RunAsync<RecordWriteResult>(
                key.Keyspace,
                (id, token) => CompareAndSwapInKeyspaceAsync(id, request, expectedRevision, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Swaps a record of a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the write needs.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the swap.</returns>
    private async ValueTask<RecordWriteResult> CompareAndSwapInKeyspaceAsync(
        long id,
        PgPutRequest request,
        long expectedRevision,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) =>
                    CompareAndSwapInTransactionAsync(
                        transaction,
                        id,
                        request,
                        expectedRevision,
                        token
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides a swap from the key's row and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the write needs.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the swap.</returns>
    private async ValueTask<RecordWriteResult> CompareAndSwapInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgPutRequest request,
        long expectedRevision,
        CancellationToken cancellationToken
    )
    {
        var start = await Table
            .BeginAsync(transaction, id, [request.Key], request.Sessions, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        var ended = start.EndExpiredSessions();
        start.Rows.TryGetValue(request.Key, out var row);
        var liveRow = TakeLiveRow(change, row);
        var bindsToEndedSession = BindsToEndedSession(
            start,
            ended,
            request.Lifetime,
            request.Session
        );

        var result = DecideSwap(request, change, liveRow, expectedRevision, bindsToEndedSession);

        await Table.CommitAsync(transaction, change, id, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        PgCall.Check(options, cancellationToken);

        var expectedRevision = expected.Value;

        return await Table
            .Keyspaces.RunAsync<RecordDeleteStatus>(
                key.Keyspace,
                (id, token) => CompareAndDeleteInKeyspaceAsync(id, key, expectedRevision, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a record of a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The status of the delete.</returns>
    private async ValueTask<RecordDeleteStatus> CompareAndDeleteInKeyspaceAsync(
        long id,
        CoordinationKey key,
        long expectedRevision,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) =>
                    CompareAndDeleteInTransactionAsync(
                        transaction,
                        id,
                        key,
                        expectedRevision,
                        token
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides a delete from the key's row and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The status of the delete.</returns>
    private async ValueTask<RecordDeleteStatus> CompareAndDeleteInTransactionAsync(
        PgTransaction transaction,
        long id,
        CoordinationKey key,
        long expectedRevision,
        CancellationToken cancellationToken
    )
    {
        var start = await Table
            .BeginAsync(transaction, id, [key], [], cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(key, out var row);
        var liveRow = TakeLiveRow(change, row);

        var status = DecideDelete(change, liveRow, expectedRevision);

        await Table.CommitAsync(transaction, change, id, cancellationToken).ConfigureAwait(false);
        return status;
    }

    /// <inheritdoc />
    public async ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await Table
            .Keyspaces.RunAsync<StoredRecord?>(
                key.Keyspace,
                (id, token) => TakeInKeyspaceAsync(id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a record of a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The record that was taken, or null when the key has no live row.</returns>
    private async ValueTask<StoredRecord?> TakeInKeyspaceAsync(
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .Server.TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => TakeInTransactionAsync(transaction, id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, takes the key's live row and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The record that was taken, or null when the key has no live row.</returns>
    private async ValueTask<StoredRecord?> TakeInTransactionAsync(
        PgTransaction transaction,
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var start = await Table
            .BeginAsync(transaction, id, [key], [], cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(key, out var row);
        var liveRow = TakeLiveRow(change, row);

        var taken = DecideTake(change, key, liveRow);

        await Table.CommitAsync(transaction, change, id, cancellationToken).ConfigureAwait(false);
        return taken;
    }

    /// <inheritdoc />
    public async ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(page);

        var prefixIndex = PgKeys.Index(prefix);
        var (startIndex, startDepth) = ResolveStart(page, prefixIndex);

        PgCall.Check(options, cancellationToken);

        var prefixEnd = PgKeys.PrefixEnd(prefixIndex);
        var request = new PgListRequest(
            prefix,
            page.Limit,
            prefixIndex,
            prefixEnd,
            startIndex,
            startDepth
        );

        return await Table
            .Keyspaces.RunAsync<RecordPage>(
                prefix.Keyspace,
                (id, token) => ListInKeyspaceAsync(id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists a page of the records of a keyspace, again after the keyspace's writers when the first read met an
    /// expired row.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">The request for the page.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The page.</returns>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    private async ValueTask<RecordPage> ListInKeyspaceAsync(
        long id,
        PgListRequest request,
        CancellationToken cancellationToken
    )
    {
        var statement = ListStatement(id, request);
        var first = await Table
            .Server.ExecuteAsync([statement], ReadListAsync, cancellationToken)
            .ConfigureAwait(false);

        if (first.Length == 0)
        {
            throw new PgKeyspaceMissingException(PgKeyspaceMissingException.RowMissingMessage);
        }

        var read = first;
        if (HoldsExpiredRow(first, request.Limit))
        {
            read = await Table
                .ReadAfterWritersAsync(id, statement, ReadListAsync, cancellationToken)
                .ConfigureAwait(false);

            if (read.Length == 0)
            {
                throw new PgKeyspaceMissingException(PgKeyspaceMissingException.RowMissingMessage);
            }
        }

        return ToPage(request, read);
    }

    /// <summary>
    /// Builds the statement that lists the rows of a page.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">The request for the page.</param>
    /// <returns>The statement, which reads one row more than the page holds.</returns>
    private PgStatement ListStatement(long id, PgListRequest request)
    {
        var prefixIndexArgument = PgArgument.Typed(request.PrefixIndex, NpgsqlDbType.Bytea);
        var prefixEndArgument = PgArgument.Typed(request.PrefixEnd, NpgsqlDbType.Bytea);
        var startIndexArgument = PgArgument.Typed(request.StartIndex, NpgsqlDbType.Bytea);
        var startDepthArgument = PgArgument.Typed(request.StartDepth, NpgsqlDbType.Smallint);
        var limitArgument = PgArgument.Typed(request.Limit + 1, NpgsqlDbType.Integer);

        return new PgStatement(
            ListSql,
            id,
            prefixIndexArgument,
            prefixEndArgument,
            startIndexArgument,
            startDepthArgument,
            limitArgument
        );
    }
}
