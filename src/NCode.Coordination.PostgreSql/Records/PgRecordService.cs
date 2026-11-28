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
    /// Gets the id of the session that a lifetime binds a record to, when the session lives in the record's keyspace.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The session's id, or null when the lifetime binds to no session or to one of another keyspace, which
    /// the keyspace's rows can't hold.</returns>
    private static string? SessionOf(CoordinationKey key, RecordLifetime lifetime) =>
        lifetime.Session is { } session && session.Keyspace == key.Keyspace ? session.Value : null;

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
    /// Gets the row of a key when it is live.
    /// </summary>
    /// <param name="row">The key's row, or null when the key has none.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <returns>The row when it is live; otherwise, null.</returns>
    private static PgRecordRow? LiveRowOf(PgRecordRow? row, DateTime now) =>
        row?.StateAt(now) == PgRecordState.Live ? row : null;

    /// <summary>
    /// Logs the removal of a key's row when it has expired.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="row">The key's row, or null when the key has none.</param>
    private static void ExpireIfExpired(PgKeyspaceChange change, PgRecordRow? row)
    {
        if (row is not null && row.StateAt(change.Now) == PgRecordState.Expired)
        {
            change.Expire([row]);
        }
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
        var put = new PgRecordPut(request.Key, request.Value, deadline, request.Session)
        {
            ReplacedSession = replacedSession,
        };
        change.Put(put);

        var written = new Revision(revision);
        return RecordWriteResult.Written(written);
    }

    /// <summary>
    /// Decides whether a create is refused, from the rows that were read.
    /// </summary>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="bindsToEndedSession">Whether the lifetime binds to a session that is not live.</param>
    /// <returns>The result that refuses the create, or null when the create may write.</returns>
    private static RecordWriteResult? RefuseCreate(PgRecordRow? liveRow, bool bindsToEndedSession)
    {
        if (bindsToEndedSession)
        {
            return RecordWriteResult.SessionEnded;
        }

        return liveRow is null ? null : RecordWriteResult.Conflict;
    }

    /// <summary>
    /// Decides whether a compare and swap is refused, from the rows that were read.
    /// </summary>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <param name="bindsToEndedSession">Whether the lifetime binds to a session that is not live.</param>
    /// <returns>The result that refuses the swap, or null when the swap may write.</returns>
    private static RecordWriteResult? RefuseSwap(
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

        return liveRow.Revision == expectedRevision ? null : RecordWriteResult.Conflict;
    }

    /// <summary>
    /// Decides whether a compare and delete is refused, from the rows that were read.
    /// </summary>
    /// <param name="liveRow">The key's row when it is live; otherwise, null.</param>
    /// <param name="expectedRevision">The revision that the record must have.</param>
    /// <returns>The status that refuses the delete, or null when the delete may write.</returns>
    private static RecordDeleteStatus? RefuseDelete(PgRecordRow? liveRow, long expectedRevision)
    {
        if (liveRow is null)
        {
            return RecordDeleteStatus.NotFound;
        }

        return liveRow.Revision == expectedRevision ? null : RecordDeleteStatus.Conflict;
    }

    /// <summary>
    /// Takes a revision and deletes a live row in it.
    /// </summary>
    /// <param name="change">The change that the transaction builds.</param>
    /// <param name="liveRow">The row to delete.</param>
    private static void DeleteRecord(PgKeyspaceChange change, PgRecordRow liveRow)
    {
        _ = change.Begin();
        change.Delete(liveRow);
    }

    /// <summary>
    /// Builds the record that a row holds.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="row">The record's row.</param>
    /// <returns>The record.</returns>
    private static StoredRecord ToStoredRecord(CoordinationKey key, PgRecordRow row)
    {
        var revision = new Revision(row.Revision);
        return new StoredRecord(key, row.Value, revision);
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
            return (prefixIndex, PgKeys.BeforeFirstDepth);
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
        var now = PgColumns.ReadUtc(reader, 1);
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
        var counted = 0;
        foreach (var item in items)
        {
            if (item.Row is null)
            {
                continue;
            }

            if (counted == limit)
            {
                return false;
            }

            counted++;
            if (item.StateAt() is PgRecordState.Expired or PgRecordState.SessionExpired)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lists the live records of a page.
    /// </summary>
    /// <param name="request">The request for the page.</param>
    /// <param name="listed">The items that have a row, in order.</param>
    /// <returns>The live records among the first items that the page holds.</returns>
    private static List<StoredRecord> ReadLiveRecords(
        PgListRequest request,
        List<PgListItem> listed
    )
    {
        var count = Math.Min(request.Limit, listed.Count);
        var records = new List<StoredRecord>(count);
        for (var position = 0; position < count; position++)
        {
            var item = listed[position];
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
    private static string? NextPageToken(List<PgListItem> listed, int limit)
    {
        if (listed.Count > limit && listed[limit - 1].Row is { } last)
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
        var listed = new List<PgListItem>(read.Length);
        foreach (var item in read)
        {
            if (item.Row is not null)
            {
                listed.Add(item);
            }
        }

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
    private string ListSql { get; } = table.Apply(ListRows);

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
        PgCall.CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);

        var copy = value.ToArray();
        var session = SessionOf(key, lifetime);
        var request = new PgPutRequest(key, copy, lifetime, session);

        return await Table
            .RunInKeyspaceAsync<RecordWriteResult>(
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
            .TransactAsync(
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
        var scope = new PgRecordScope(id, [request.Key], [.. request.Sessions]);
        var start = await Table
            .BeginAsync(transaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(request.Key, out var row);
        var liveRow = LiveRowOf(row, change.Now);
        ExpireIfExpired(change, row);
        var bindsToEndedSession = start.BindsToEndedSession(request.Lifetime, request.Session);

        var refusal = RefuseCreate(liveRow, bindsToEndedSession);
        var replacedSession = ReplacedSession(row, change.Now);
        var result = refusal ?? WriteRecord(request, change, replacedSession);

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
        PgCall.CheckValue(value, nameof(value));
        PgCall.Check(options, cancellationToken);

        var copy = value.ToArray();
        var expectedRevision = expected.Value;
        var session = SessionOf(key, lifetime);
        var request = new PgPutRequest(key, copy, lifetime, session);

        return await Table
            .RunInKeyspaceAsync<RecordWriteResult>(
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
            .TransactAsync(
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
        var scope = new PgRecordScope(id, [request.Key], [.. request.Sessions]);
        var start = await Table
            .BeginAsync(transaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(request.Key, out var row);
        var liveRow = LiveRowOf(row, change.Now);
        ExpireIfExpired(change, row);
        var bindsToEndedSession = start.BindsToEndedSession(request.Lifetime, request.Session);

        var refusal = RefuseSwap(liveRow, expectedRevision, bindsToEndedSession);
        var result = refusal ?? WriteRecord(request, change, null);

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
            .RunInKeyspaceAsync<RecordDeleteStatus>(
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
            .TransactAsync(
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
        var scope = new PgRecordScope(id, [key], []);
        var start = await Table
            .BeginAsync(transaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(key, out var row);
        var liveRow = LiveRowOf(row, change.Now);
        ExpireIfExpired(change, row);

        var refusal = RefuseDelete(liveRow, expectedRevision);
        if (refusal is null && liveRow is not null)
        {
            DeleteRecord(change, liveRow);
        }

        var status = refusal ?? RecordDeleteStatus.Deleted;

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
            .RunInKeyspaceAsync<StoredRecord?>(
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
            .TransactAsync(
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
        var scope = new PgRecordScope(id, [key], []);
        var start = await Table
            .BeginAsync(transaction, scope, cancellationToken)
            .ConfigureAwait(false);

        var change = start.Change;
        start.EndExpiredSessions();
        start.Rows.TryGetValue(key, out var row);
        var liveRow = LiveRowOf(row, change.Now);
        ExpireIfExpired(change, row);

        StoredRecord? taken = null;
        if (liveRow is not null)
        {
            DeleteRecord(change, liveRow);
            taken = ToStoredRecord(key, liveRow);
        }

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
            .RunInKeyspaceAsync<RecordPage>(
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
            .ExecuteAsync([statement], ReadListAsync, cancellationToken)
            .ConfigureAwait(false);

        PgKeyspaceMissingException.ThrowIfNoRow(first.Length);

        var read = first;
        if (HoldsExpiredRow(first, request.Limit))
        {
            read = await Table
                .ReadAfterWritersAsync(id, statement, ReadListAsync, cancellationToken)
                .ConfigureAwait(false);

            PgKeyspaceMissingException.ThrowIfNoRow(read.Length);
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
