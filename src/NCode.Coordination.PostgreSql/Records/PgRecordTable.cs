// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The reads that the record and transaction services share: it begins a change of one keyspace's records, reading
/// the keyspace's revision, the server's instant and the rows of the keys, and it commits the change.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The keyspaces' ids.</param>
/// <param name="settings">The settings of the provider.</param>
internal sealed class PgRecordTable(PgServer server, PgKeyspaces keyspaces, PgSettings settings)
{
    /// <summary>
    /// The template of the statement that locks the keyspace's row and reads its revision.
    /// </summary>
    private const string SelectRevision = """
        SELECT revision FROM {schema}.keyspaces WHERE id = $1 FOR NO KEY UPDATE
        """;

    /// <summary>
    /// The template of the statement that reads the server's instant.
    /// </summary>
    private const string SelectClock = "SELECT clock_timestamp()";

    /// <summary>
    /// The template of the statement that reads the rows of keys, beside the deadlines of their sessions.
    /// </summary>
    private const string SelectRows = """
        SELECT r.idx, r.depth, r.key, r.value, r.revision, r.deadline, r.session_id, s.deadline
        FROM {schema}.records r
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = r.keyspace_id AND s.id = r.session_id
        WHERE r.keyspace_id = $1 AND (r.idx, r.depth) IN (SELECT * FROM unnest($2::bytea[], $3::smallint[]))
        """;

    /// <summary>
    /// The template of the statement that reads the deadlines of the sessions that the writes bind to.
    /// </summary>
    private const string SelectSessions = """
        SELECT id, deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = ANY($2::text[])
        """;

    /// <summary>
    /// The template of the statement that reads the server's instant and one record's row, if it has one.
    /// </summary>
    private const string SelectOne = """
        SELECT now.t, r.value, r.revision, r.deadline, r.session_id, s.deadline
        FROM (SELECT clock_timestamp() AS t) now
        LEFT JOIN {schema}.records r ON r.keyspace_id = $1 AND r.idx = $2 AND r.depth = $3
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = r.keyspace_id AND s.id = r.session_id
        """;

    /// <summary>
    /// The template of the statement that waits for the writers of a keyspace's row.
    /// </summary>
    private const string LockShare = """
        SELECT 1 FROM {schema}.keyspaces WHERE id = $1 FOR SHARE
        """;

    /// <summary>
    /// Reads a text column that can be null.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The text, or null when the column is null.</returns>
    private static string? ReadNullableText(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Reads the row of a key from eight columns: the index value, the depth, the key's text, the value, the
    /// revision, the deadline, the session's id and the session's deadline.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="first">The ordinal of the first of the eight columns.</param>
    /// <returns>The row.</returns>
    public static PgRecordRow ReadRowAt(NpgsqlDataReader reader, int first)
    {
        var index = reader.GetFieldValue<byte[]>(first);
        var depth = reader.GetInt16(first + 1);
        var key = reader.GetString(first + 2);
        var value = reader.GetFieldValue<byte[]>(first + 3);
        var revision = reader.GetInt64(first + 4);
        var deadline = PgColumns.ReadNullableUtc(reader, first + 5);
        var session = ReadNullableText(reader, first + 6);
        var sessionDeadline = PgColumns.ReadNullableUtc(reader, first + 7);

        return new PgRecordRow(
            index,
            depth,
            key,
            value,
            revision,
            deadline,
            session,
            sessionDeadline
        );
    }

    /// <summary>
    /// Reads the row of a key from the first eight columns.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row.</returns>
    private static PgRecordRow ReadRow(NpgsqlDataReader reader)
    {
        return ReadRowAt(reader, 0);
    }

    /// <summary>
    /// Reads the id and the deadline of a session from the first two columns.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The session's id and its deadline.</returns>
    private static (string Id, DateTime Deadline) ReadSessionDeadline(NpgsqlDataReader reader)
    {
        var id = reader.GetString(0);
        var deadline = PgColumns.ReadUtc(reader, 1);

        return (id, deadline);
    }

    /// <summary>
    /// Reads the row of the result of <see cref="SelectOne"/>, when the key has one.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row, or null when the key has none.</returns>
    private static PgOneRead? ReadOne(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(1))
        {
            return null;
        }

        var now = PgColumns.ReadUtc(reader, 0);
        var value = reader.GetFieldValue<byte[]>(1);
        var revision = reader.GetInt64(2);
        var deadline = PgColumns.ReadNullableUtc(reader, 3);
        var session = ReadNullableText(reader, 4);
        var sessionDeadline = PgColumns.ReadNullableUtc(reader, 5);

        return new PgOneRead(now, value, revision, deadline, session, sessionDeadline);
    }

    /// <summary>
    /// Reads the result of <see cref="SelectOne"/>.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The row and the instant, or null when the key has no row.</returns>
    private static async ValueTask<PgOneRead?> ReadOneResultAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(ReadOne, cancellationToken).ConfigureAwait(false);

        return rows.Length == 0 ? null : rows[0];
    }

    /// <summary>
    /// Reads the results of the first batch of a change: the revision, the instant, the rows and the sessions.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What the batch's four statements returned.</returns>
    private static async ValueTask<PgStartRead> ReadStartAsync(
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
        var rows = await results.ReadAsync(ReadRow, cancellationToken).ConfigureAwait(false);
        var sessions = await results
            .ReadAsync(ReadSessionDeadline, cancellationToken)
            .ConfigureAwait(false);

        return new PgStartRead(revisions, clocks, rows, sessions);
    }

    /// <summary>
    /// Reads the row of the lock statement, which holds nothing that the caller uses, and then the results that
    /// follow it.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="readResults"/> returns.</typeparam>
    /// <param name="results">The results of the batch.</param>
    /// <param name="readResults">Reads the results that follow the lock statement's.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="readResults"/> returned.</returns>
    private static async ValueTask<T> ReadPastLockAsync<T>(
        PgResults results,
        Func<PgResults, CancellationToken, ValueTask<T>> readResults,
        CancellationToken cancellationToken
    )
    {
        await results.SkipAsync(cancellationToken).ConfigureAwait(false);

        return await readResults(results, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a row that was read to the record it holds.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="read">The row.</param>
    /// <returns>The record.</returns>
    private static StoredRecord ToRecord(CoordinationKey key, PgOneRead read)
    {
        var revision = new Revision(read.Revision);

        return new StoredRecord(key, read.Value, revision);
    }

    /// <summary>
    /// Decodes the key of a row that the server returned.
    /// </summary>
    /// <param name="keyspace">The keyspace the key belongs to.</param>
    /// <param name="text">The key's text.</param>
    /// <param name="depth">The key's partition depth.</param>
    /// <returns>The key.</returns>
    /// <exception cref="CoordinationException">The text is not one that the provider writes; the call did not apply.</exception>
    public static CoordinationKey DecodeKey(Keyspace keyspace, string text, int depth)
    {
        if (PgKeys.TryDecodeKey(keyspace, text, depth, out var key))
        {
            return key;
        }

        throw new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            false,
            "The server returned a record key the provider didn't write."
        );
    }

    /// <summary>
    /// Finds the rows that the asked keys hold, by key.
    /// </summary>
    /// <param name="keys">The asked keys, each once.</param>
    /// <param name="indexes">The index value of each key.</param>
    /// <param name="depths">The partition depth of each key.</param>
    /// <param name="rows">The rows that were read.</param>
    /// <returns>The rows of the keys that exist, expired or not, by key.</returns>
    private static ImmutableDictionary<CoordinationKey, PgRecordRow> FindRows(
        CoordinationKey[] keys,
        byte[][] indexes,
        short[] depths,
        ImmutableArray<PgRecordRow> rows
    )
    {
        var byIdentity = new Dictionary<PgRowIdentity, PgRecordRow>(rows.Length);
        foreach (var row in rows)
        {
            var identity = new PgRowIdentity(row.Index, row.Depth);
            byIdentity[identity] = row;
        }

        var found = ImmutableDictionary.CreateBuilder<CoordinationKey, PgRecordRow>();
        for (var position = 0; position < keys.Length; position++)
        {
            var identity = new PgRowIdentity(indexes[position], depths[position]);
            if (byIdentity.TryGetValue(identity, out var row))
            {
                found[keys[position]] = row;
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// Gets the keys of a scope without repeats, each in the place where it first appears.
    /// </summary>
    /// <param name="keys">The scope's keys.</param>
    /// <returns>The distinct keys, in first-seen order.</returns>
    private static CoordinationKey[] DistinctKeys(ImmutableArray<CoordinationKey> keys)
    {
        var seen = new HashSet<CoordinationKey>(keys.Length);
        var distinct = new List<CoordinationKey>(keys.Length);
        foreach (var key in keys)
        {
            if (seen.Add(key))
            {
                distinct.Add(key);
            }
        }

        return distinct.ToArray();
    }

    /// <summary>
    /// Gets the index value and the partition depth of each key.
    /// </summary>
    /// <param name="keys">The keys.</param>
    /// <returns>The index value of each key and the partition depth of each key, in the keys' order.</returns>
    private static (byte[][] Indexes, short[] Depths) IndexesAndDepthsOf(CoordinationKey[] keys)
    {
        var indexes = new byte[keys.Length][];
        var depths = new short[keys.Length];
        for (var position = 0; position < keys.Length; position++)
        {
            var key = keys[position];
            indexes[position] = PgKeys.Index(key);
            depths[position] = (short)key.PartitionDepth;
        }

        return (indexes, depths);
    }

    /// <summary>
    /// Gets the ids of the sessions of a scope without repeats, each in the place where it first appears.
    /// </summary>
    /// <param name="sessions">The scope's session ids.</param>
    /// <returns>The distinct ids, in first-seen order.</returns>
    private static string[] DistinctSessions(ImmutableArray<string> sessions)
    {
        var seen = new HashSet<string>(sessions.Length, StringComparer.Ordinal);
        var distinct = new List<string>(sessions.Length);
        foreach (var session in sessions)
        {
            if (seen.Add(session))
            {
                distinct.Add(session);
            }
        }

        return distinct.ToArray();
    }

    /// <summary>
    /// Gets the deadlines of the sessions that exist, by session id.
    /// </summary>
    /// <param name="sessions">The id and deadline of each session whose row exists.</param>
    /// <returns>The deadlines, by session id.</returns>
    private static ImmutableDictionary<string, DateTime> DeadlinesOf(
        ImmutableArray<(string Id, DateTime Deadline)> sessions
    )
    {
        var deadlines = ImmutableDictionary.CreateBuilder<string, DateTime>(StringComparer.Ordinal);
        foreach (var (id, deadline) in sessions)
        {
            deadlines[id] = deadline;
        }

        return deadlines.ToImmutable();
    }

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    public PgSettings Settings { get; } = settings;

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server;

    /// <summary>
    /// Gets the keyspaces' ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; } = keyspaces;

    /// <summary>
    /// Gets the statement that locks the keyspace's row and reads its revision.
    /// </summary>
    private string SelectRevisionSql { get; } = settings.Schema.Apply(SelectRevision);

    /// <summary>
    /// Gets the statement that reads the server's instant.
    /// </summary>
    private string SelectClockSql { get; } = settings.Schema.Apply(SelectClock);

    /// <summary>
    /// Gets the statement that reads the rows of keys.
    /// </summary>
    private string SelectRowsSql { get; } = settings.Schema.Apply(SelectRows);

    /// <summary>
    /// Gets the statement that reads the deadlines of the sessions that the writes bind to.
    /// </summary>
    private string SelectSessionsSql { get; } = settings.Schema.Apply(SelectSessions);

    /// <summary>
    /// Gets the statement that reads the server's instant and one record's row.
    /// </summary>
    private string SelectOneSql { get; } = settings.Schema.Apply(SelectOne);

    /// <summary>
    /// Gets the statement that waits for the writers of a keyspace's row.
    /// </summary>
    private string LockShareSql { get; } = settings.Schema.Apply(LockShare);

    /// <summary>
    /// Builds the statement that locks a keyspace's row and reads its revision, for services whose transactions read
    /// tables of their own.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <returns>The statement, whose result holds the revision, or no row when the keyspace's row does not exist.</returns>
    public PgStatement LockKeyspace(long keyspaceId) => new(SelectRevisionSql, keyspaceId);

    /// <summary>
    /// Builds the statement that reads the server's instant.
    /// </summary>
    /// <returns>The statement, whose result holds the instant.</returns>
    public PgStatement Clock() => new(SelectClockSql);

    /// <summary>
    /// Builds the change of a keyspace that a transaction of a service with tables of its own makes.
    /// </summary>
    /// <param name="origin">The keyspace, the revision and the instant that the transaction read.</param>
    /// <returns>The change.</returns>
    public PgKeyspaceChange NewChange(PgChangeOrigin origin) => new(Settings, origin);

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
        return Keyspaces.RunAsync(keyspace, operation, cancellationToken);
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
        return Keyspaces.RunAsync(keyspace, operation, cancellationToken);
    }

    /// <summary>
    /// Resolves a keyspace's id.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The keyspace's id.</returns>
    /// <exception cref="ArgumentNullException">The keyspace is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public ValueTask<long> ResolveAsync(Keyspace keyspace, CancellationToken cancellationToken)
    {
        return Keyspaces.ResolveAsync(keyspace, cancellationToken);
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
        return Server.TransactAsync(isolation, body, cancellationToken);
    }

    /// <summary>
    /// Runs statements as one batch, which the server runs as one implicit transaction, and reads their results.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="read"/> returns.</typeparam>
    /// <param name="statements">The statements.</param>
    /// <param name="read">Reads the results.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public ValueTask<T> ExecuteAsync<T>(
        IReadOnlyList<PgStatement> statements,
        Func<PgResults, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        return Server.ExecuteAsync(statements, read, cancellationToken);
    }

    /// <summary>
    /// Runs a read after the keyspace's writers have finished: a transaction that takes a share lock on the keyspace's
    /// row, then runs the read and returns what <paramref name="readResults"/> decodes from it.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="readResults"/> returns.</typeparam>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="read">The statement that reads, which runs after the lock.</param>
    /// <param name="readResults">Reads the results of <paramref name="read"/>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="readResults"/> returned.</returns>
    /// <exception cref="ArgumentNullException">The statement or the reader is null.</exception>
    public ValueTask<T> ReadAfterWritersAsync<T>(
        long keyspaceId,
        PgStatement read,
        Func<PgResults, CancellationToken, ValueTask<T>> readResults,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(readResults);

        return Server.TransactAsync(
            PgIsolation.ReadCommitted,
            (transaction, token) =>
                ReadAfterLockAsync(transaction, keyspaceId, read, readResults, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Runs the batch that locks a keyspace's row for sharing and then reads.
    /// </summary>
    /// <typeparam name="T">The type that <paramref name="readResults"/> returns.</typeparam>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="read">The statement that reads, which runs after the lock.</param>
    /// <param name="readResults">Reads the results of <paramref name="read"/>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What <paramref name="readResults"/> returned.</returns>
    private async ValueTask<T> ReadAfterLockAsync<T>(
        PgTransaction transaction,
        long keyspaceId,
        PgStatement read,
        Func<PgResults, CancellationToken, ValueTask<T>> readResults,
        CancellationToken cancellationToken
    )
    {
        var lockStatement = new PgStatement(LockShareSql, keyspaceId);

        return await transaction
            .FirstAsync(
                [lockStatement, read],
                (results, token) => ReadPastLockAsync(results, readResults, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one record. A record that is live in the single statement is returned; one that is expired is read again
    /// in a transaction that first waits for the keyspace's writers, so the answer follows a write that replaces it.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The record, or null when it is absent or expired.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<StoredRecord?> ReadOneAsync(
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        return await Keyspaces
            .RunAsync<StoredRecord?>(
                key.Keyspace,
                (id, token) => ReadOneInKeyspaceAsync(id, key, index, depth, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one record of a keyspace. A live row is the record and a row whose session has ended is absent at once;
    /// a row that has expired, or whose session is past its deadline, is read again after the keyspace's writers and
    /// is the record only when it is then live.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The record's key.</param>
    /// <param name="index">The key's index value.</param>
    /// <param name="depth">The key's partition depth.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The record, or null when it is absent or expired.</returns>
    private async ValueTask<StoredRecord?> ReadOneInKeyspaceAsync(
        long id,
        CoordinationKey key,
        byte[] index,
        short depth,
        CancellationToken cancellationToken
    )
    {
        var statement = SelectOneStatement(id, index, depth);
        var first = await Server
            .ExecuteAsync([statement], ReadOneResultAsync, cancellationToken)
            .ConfigureAwait(false);

        if (first is null)
        {
            return null;
        }

        var firstState = first.StateAt();
        if (firstState == PgRecordState.Live)
        {
            return ToRecord(key, first);
        }

        if (firstState == PgRecordState.SessionEnded)
        {
            return null;
        }

        var second = await ReadAfterWritersAsync(
                id,
                statement,
                ReadOneResultAsync,
                cancellationToken
            )
            .ConfigureAwait(false);

        return second is not null && second.StateAt() == PgRecordState.Live
            ? ToRecord(key, second)
            : null;
    }

    /// <summary>
    /// Builds the statement that reads the server's instant and one record's row.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="index">The key's index value.</param>
    /// <param name="depth">The key's partition depth.</param>
    /// <returns>The statement.</returns>
    private PgStatement SelectOneStatement(long keyspaceId, byte[] index, short depth) =>
        new(SelectOneSql, keyspaceId, index, depth);

    /// <summary>
    /// Begins a change of a keyspace's records in a transaction: its first batch locks the keyspace's row and reads
    /// its revision, reads the server's instant, and reads the rows that the keys hold.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="scope">The keyspace, the keys whose rows to read and the sessions whose rows to read.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The change, the rows of the keys that exist, and the deadlines of the sessions that exist.</returns>
    /// <exception cref="ArgumentNullException">The transaction is null.</exception>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    public async ValueTask<PgChangeStart> BeginAsync(
        PgTransaction transaction,
        PgRecordScope scope,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var keyspaceId = scope.KeyspaceId;
        var distinct = DistinctKeys(scope.Keys);
        var (indexes, depths) = IndexesAndDepthsOf(distinct);
        var sessionIds = DistinctSessions(scope.Sessions);
        var statements = StartStatements(keyspaceId, indexes, depths, sessionIds);

        var read = await transaction
            .FirstAsync(statements, ReadStartAsync, cancellationToken)
            .ConfigureAwait(false);

        if (read.Revisions.Length == 0)
        {
            throw new PgKeyspaceMissingException(PgKeyspaceMissingException.RowMissingMessage);
        }

        var now = DateTime.SpecifyKind(read.Clocks[0], DateTimeKind.Utc);
        var origin = new PgChangeOrigin(keyspaceId, read.Revisions[0], now);
        var change = NewChange(origin);
        var found = FindRows(distinct, indexes, depths, read.Rows);
        var deadlines = DeadlinesOf(read.Sessions);

        return new PgChangeStart(change, found, deadlines);
    }

    /// <summary>
    /// Builds the statements of the first batch of a change: the revision lock, the instant, the rows of the keys
    /// and the deadlines of the sessions.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="indexes">The index value of each key.</param>
    /// <param name="depths">The partition depth of each key.</param>
    /// <param name="sessionIds">The ids of the sessions whose rows to read.</param>
    /// <returns>The four statements, in the order their results are read.</returns>
    private PgStatement[] StartStatements(
        long keyspaceId,
        byte[][] indexes,
        short[] depths,
        string[] sessionIds
    )
    {
        var indexesArgument = PgArgument.Typed(indexes, NpgsqlDbType.Array | NpgsqlDbType.Bytea);
        var depthsArgument = PgArgument.Typed(depths, NpgsqlDbType.Array | NpgsqlDbType.Smallint);
        var sessionIdsArgument = PgArgument.Typed(
            sessionIds,
            NpgsqlDbType.Array | NpgsqlDbType.Text
        );

        var revision = new PgStatement(SelectRevisionSql, keyspaceId);
        var clock = new PgStatement(SelectClock);
        var rows = new PgStatement(SelectRowsSql, keyspaceId, indexesArgument, depthsArgument);
        var sessionRows = new PgStatement(SelectSessionsSql, keyspaceId, sessionIdsArgument);

        return [revision, clock, rows, sessionRows];
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
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(change);

        if (change.IsEmpty)
        {
            return transaction.RollbackAsync(cancellationToken);
        }

        var channel = PgChannels.ForKeyspace(Settings.Schema, keyspaceId);
        var statements = change.Statements(channel);
        return transaction.CommitAsync(statements, cancellationToken);
    }
}
