// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
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
    /// <summary>The template of the statement that lists the rows under a prefix after a position.</summary>
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

    /// <summary>The template of the statement that waits for the writers of a keyspace's row.</summary>
    private const string LockShare = """
        SELECT 1 FROM {schema}.keyspaces WHERE id = $1 FOR SHARE
        """;

    /// <summary>
    /// Gets the record table that the operations share.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>Gets the statement that lists the rows under a prefix after a position.</summary>
    private string ListSql { get; } = table.Settings.Schema.Apply(ListRows);

    /// <summary>Gets the statement that waits for the writers of a keyspace's row.</summary>
    private string LockShareSql { get; } = table.Settings.Schema.Apply(LockShare);

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
        var timeToLive = lifetime.TimeToLive;
        var session = SessionOf(key, lifetime);
        string[] sessions = session is null ? [] : [session];

        return await Table
            .Keyspaces.RunAsync<RecordWriteResult>(
                key.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], sessions, transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                var ended = start.EndExpiredSessions();
                                var state = PgRecordState.Live;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    state = row.StateAt(change.Now);
                                    if (state == PgRecordState.Expired)
                                    {
                                        change.Expire([row]);
                                    }
                                }

                                if (BindsToEndedSession(start, ended, lifetime, session))
                                {
                                    await Table
                                        .CommitAsync(transaction, change, id, transactionToken)
                                        .ConfigureAwait(false);
                                    return RecordWriteResult.SessionEnded;
                                }

                                if (row is not null && state == PgRecordState.Live)
                                {
                                    await Table
                                        .CommitAsync(transaction, change, id, transactionToken)
                                        .ConfigureAwait(false);
                                    return RecordWriteResult.Conflict;
                                }

                                var revision = change.Begin();
                                DateTime? deadline = timeToLive is null
                                    ? null
                                    : change.Now.AddTicks(PgCall.ToMicroseconds(timeToLive) * 10);
                                change.Put(
                                    key,
                                    copy,
                                    deadline,
                                    session,
                                    ReplacedSession(row, change.Now)
                                );

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return RecordWriteResult.Written(new Revision(revision));
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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
        var timeToLive = lifetime.TimeToLive;
        var expectedRevision = expected.Value;
        var session = SessionOf(key, lifetime);
        string[] sessions = session is null ? [] : [session];

        return await Table
            .Keyspaces.RunAsync<RecordWriteResult>(
                key.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], sessions, transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                var ended = start.EndExpiredSessions();
                                var live = false;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    var state = row.StateAt(change.Now);
                                    if (state == PgRecordState.Live)
                                    {
                                        live = true;
                                    }
                                    else if (state == PgRecordState.Expired)
                                    {
                                        change.Expire([row]);
                                    }
                                }

                                RecordWriteResult result;
                                if (BindsToEndedSession(start, ended, lifetime, session))
                                {
                                    result = RecordWriteResult.SessionEnded;
                                }
                                else if (!live)
                                {
                                    result = RecordWriteResult.NotFound;
                                }
                                else if (row is null || row.Revision != expectedRevision)
                                {
                                    result = RecordWriteResult.Conflict;
                                }
                                else
                                {
                                    var revision = change.Begin();
                                    DateTime? deadline = timeToLive is null
                                        ? null
                                        : change.Now.AddTicks(
                                            PgCall.ToMicroseconds(timeToLive) * 10
                                        );
                                    change.Put(key, copy, deadline, session, null);
                                    result = RecordWriteResult.Written(new Revision(revision));
                                }

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return result;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], [], transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                start.EndExpiredSessions();
                                var status = RecordDeleteStatus.NotFound;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    var state = row.StateAt(change.Now);
                                    if (state == PgRecordState.Live)
                                    {
                                        if (row.Revision == expectedRevision)
                                        {
                                            change.Begin();
                                            change.Delete(row);
                                            status = RecordDeleteStatus.Deleted;
                                        }
                                        else
                                        {
                                            status = RecordDeleteStatus.Conflict;
                                        }
                                    }
                                    else if (state == PgRecordState.Expired)
                                    {
                                        change.Expire([row]);
                                    }
                                }

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return status;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await Table
                                    .BeginAsync(transaction, id, [key], [], transactionToken)
                                    .ConfigureAwait(false);

                                var change = start.Change;
                                start.EndExpiredSessions();
                                StoredRecord? taken = null;
                                if (start.Rows.TryGetValue(key, out var row))
                                {
                                    var state = row.StateAt(change.Now);
                                    if (state == PgRecordState.Live)
                                    {
                                        change.Begin();
                                        change.Delete(row);
                                        taken = new StoredRecord(
                                            key,
                                            row.Value,
                                            new Revision(row.Revision)
                                        );
                                    }
                                    else if (state == PgRecordState.Expired)
                                    {
                                        change.Expire([row]);
                                    }
                                }

                                await Table
                                    .CommitAsync(transaction, change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return taken;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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
        byte[] startIndex;
        short startDepth;
        if (page.ContinuationToken is null)
        {
            startIndex = prefixIndex;
            startDepth = -1;
        }
        else if (
            PgKeys.TryReadPageToken(
                page.ContinuationToken,
                prefixIndex,
                out var tokenIndex,
                out var tokenDepth
            )
        )
        {
            startIndex = tokenIndex;
            startDepth = (short)tokenDepth;
        }
        else
        {
            throw new ArgumentException(
                "The continuation token was not issued by the PostgreSQL provider for this prefix.",
                nameof(page)
            );
        }

        PgCall.Check(options, cancellationToken);

        var limit = page.Limit;
        var prefixEnd = PgKeys.PrefixEnd(prefixIndex);
        var prefixIndexArgument = PgArgument.Typed(prefixIndex, NpgsqlDbType.Bytea);
        var prefixEndArgument = PgArgument.Typed(prefixEnd, NpgsqlDbType.Bytea);
        var startIndexArgument = PgArgument.Typed(startIndex, NpgsqlDbType.Bytea);
        var startDepthArgument = PgArgument.Typed(startDepth, NpgsqlDbType.Smallint);
        var limitArgument = PgArgument.Typed(limit + 1, NpgsqlDbType.Integer);

        return await Table
            .Keyspaces.RunAsync<RecordPage>(
                prefix.Keyspace,
                async (id, token) =>
                {
                    PgStatement Statement() =>
                        new(
                            ListSql,
                            id,
                            prefixIndexArgument,
                            prefixEndArgument,
                            startIndexArgument,
                            startDepthArgument,
                            limitArgument
                        );

                    var first = await Table
                        .Server.ExecuteAsync([Statement()], ReadListAsync, token)
                        .ConfigureAwait(false);

                    if (first.Length == 0)
                    {
                        throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
                    }

                    var read = first;
                    if (
                        first
                            .Where(item => item.Row is not null)
                            .Take(limit)
                            .Any(item =>
                                item.StateAt()
                                    is PgRecordState.Expired
                                        or PgRecordState.SessionExpired
                            )
                    )
                    {
                        read = await Table
                            .Server.TransactAsync(
                                PgIsolation.ReadCommitted,
                                async (transaction, transactionToken) =>
                                    await transaction
                                        .FirstAsync(
                                            [new PgStatement(LockShareSql, id), Statement()],
                                            async (results, readToken) =>
                                            {
                                                await results
                                                    .ReadAsync(
                                                        reader => reader.GetInt32(0),
                                                        readToken
                                                    )
                                                    .ConfigureAwait(false);
                                                return await ReadListAsync(results, readToken)
                                                    .ConfigureAwait(false);
                                            },
                                            transactionToken
                                        )
                                        .ConfigureAwait(false),
                                token
                            )
                            .ConfigureAwait(false);

                        if (read.Length == 0)
                        {
                            throw new PgKeyspaceMissingException(
                                "The keyspace's row does not exist."
                            );
                        }
                    }

                    var listed = read.Where(item => item.Row is not null).ToArray();
                    var records = new List<StoredRecord>();
                    foreach (var item in listed.Take(limit))
                    {
                        if (item.Row is { } row && item.StateAt() == PgRecordState.Live)
                        {
                            var key = PgRecordTable.DecodeKey(prefix.Keyspace, row.Key, row.Depth);
                            records.Add(
                                new StoredRecord(key, row.Value, new Revision(row.Revision))
                            );
                        }
                    }

                    string? next = null;
                    if (listed.Length > limit && listed[limit - 1].Row is { } last)
                    {
                        next = PgKeys.PageToken(last.Index, last.Depth);
                    }

                    return new RecordPage(new Revision(read[0].KeyspaceRevision), records)
                    {
                        ContinuationToken = next,
                    };
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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
    ) =>
        await results
            .ReadAsync(
                reader => new PgListItem(
                    reader.GetInt64(0),
                    DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                    reader.IsDBNull(2)
                        ? null
                        : new PgRecordRow(
                            reader.GetFieldValue<byte[]>(2),
                            reader.GetInt16(3),
                            reader.GetString(4),
                            reader.GetFieldValue<byte[]>(5),
                            reader.GetInt64(6),
                            reader.IsDBNull(7)
                                ? null
                                : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
                            reader.IsDBNull(8) ? null : reader.GetString(8),
                            reader.IsDBNull(9)
                                ? null
                                : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc)
                        )
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
}
