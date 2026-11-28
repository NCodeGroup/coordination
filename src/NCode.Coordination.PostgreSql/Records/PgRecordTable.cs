// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
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
    /// <summary>The template of the statement that locks the keyspace's row and reads its revision.</summary>
    private const string SelectRevision = """
        SELECT revision FROM {schema}.keyspaces WHERE id = $1 FOR NO KEY UPDATE
        """;

    /// <summary>The template of the statement that reads the server's instant.</summary>
    private const string SelectClock = "SELECT clock_timestamp()";

    /// <summary>The template of the statement that reads the rows of keys, beside the deadlines of their sessions.</summary>
    private const string SelectRows = """
        SELECT r.idx, r.depth, r.key, r.value, r.revision, r.deadline, r.session_id, s.deadline
        FROM {schema}.records r
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = r.keyspace_id AND s.id = r.session_id
        WHERE r.keyspace_id = $1 AND (r.idx, r.depth) IN (SELECT * FROM unnest($2::bytea[], $3::smallint[]))
        """;

    /// <summary>The template of the statement that reads the deadlines of the sessions that the writes bind to.</summary>
    private const string SelectSessions = """
        SELECT id, deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = ANY($2::text[])
        """;

    /// <summary>The template of the statement that reads the server's instant and one record's row, if it has one.</summary>
    private const string SelectOne = """
        SELECT now.t, r.value, r.revision, r.deadline, r.session_id, s.deadline
        FROM (SELECT clock_timestamp() AS t) now
        LEFT JOIN {schema}.records r ON r.keyspace_id = $1 AND r.idx = $2 AND r.depth = $3
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = r.keyspace_id AND s.id = r.session_id
        """;

    /// <summary>The template of the statement that waits for the writers of a keyspace's row.</summary>
    private const string LockShare = """
        SELECT 1 FROM {schema}.keyspaces WHERE id = $1 FOR SHARE
        """;

    /// <summary>Gets the seam to the server.</summary>
    public PgServer Server { get; } = server;

    /// <summary>Gets the keyspaces' ids.</summary>
    public PgKeyspaces Keyspaces { get; } = keyspaces;

    /// <summary>Gets the settings of the provider.</summary>
    public PgSettings Settings { get; } = settings;

    /// <summary>Gets the statement that locks the keyspace's row and reads its revision.</summary>
    private string SelectRevisionSql { get; } = settings.Schema.Apply(SelectRevision);

    /// <summary>Gets the statement that reads the server's instant.</summary>
    private string SelectClockSql { get; } = settings.Schema.Apply(SelectClock);

    /// <summary>Gets the statement that reads the rows of keys.</summary>
    private string SelectRowsSql { get; } = settings.Schema.Apply(SelectRows);

    /// <summary>Gets the statement that reads the deadlines of the sessions that the writes bind to.</summary>
    private string SelectSessionsSql { get; } = settings.Schema.Apply(SelectSessions);

    /// <summary>Gets the statement that reads the server's instant and one record's row.</summary>
    private string SelectOneSql { get; } = settings.Schema.Apply(SelectOne);

    /// <summary>Gets the statement that waits for the writers of a keyspace's row.</summary>
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
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="revision">The keyspace's revision that the transaction read.</param>
    /// <param name="now">The instant the transaction read on the server's clock.</param>
    /// <returns>The change.</returns>
    public PgKeyspaceChange NewChange(long keyspaceId, long revision, DateTime now) =>
        new(Settings.Schema, keyspaceId, revision, now, Settings.LogRetention);

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
                async (id, token) =>
                {
                    var first = await Server
                        .ExecuteAsync(
                            [SelectOneStatement(id, index, depth)],
                            ReadOneResultAsync,
                            token
                        )
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

                    var second = await Server
                        .TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                                await transaction
                                    .FirstAsync(
                                        [
                                            new PgStatement(LockShareSql, id),
                                            SelectOneStatement(id, index, depth),
                                        ],
                                        async (results, readToken) =>
                                        {
                                            await results
                                                .ReadAsync(reader => reader.GetInt32(0), readToken)
                                                .ConfigureAwait(false);
                                            return await ReadOneResultAsync(results, readToken)
                                                .ConfigureAwait(false);
                                        },
                                        transactionToken
                                    )
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return second is not null && second.StateAt() == PgRecordState.Live
                        ? ToRecord(key, second)
                        : null;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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
        var rows = await results
            .ReadAsync(
                reader =>
                    reader.IsDBNull(1)
                        ? null
                        : new PgOneRead(
                            DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                            reader.GetFieldValue<byte[]>(1),
                            reader.GetInt64(2),
                            reader.IsDBNull(3)
                                ? null
                                : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                            reader.IsDBNull(4) ? null : reader.GetString(4),
                            reader.IsDBNull(5)
                                ? null
                                : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)
                        ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return rows.Length == 0 ? null : rows[0];
    }

    /// <summary>
    /// Converts a row that was read to the record it holds.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="read">The row.</param>
    /// <returns>The record.</returns>
    private static StoredRecord ToRecord(CoordinationKey key, PgOneRead read) =>
        new(key, read.Value, new Revision(read.Revision));

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
    /// Begins a change of a keyspace's records in a transaction: its first batch locks the keyspace's row and reads
    /// its revision, reads the server's instant, and reads the rows that the keys hold.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="keys">The keys whose rows to read; a key that is listed twice is read once.</param>
    /// <param name="sessions">The ids of the sessions that the writes bind to, whose rows to read.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The change, the rows of the keys that exist, and the deadlines of the sessions that exist.</returns>
    /// <exception cref="ArgumentNullException">The transaction, the keys or the sessions are null.</exception>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    public async ValueTask<PgChangeStart> BeginAsync(
        PgTransaction transaction,
        long keyspaceId,
        IReadOnlyList<CoordinationKey> keys,
        IReadOnlyCollection<string> sessions,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(sessions);

        var distinct = keys.Distinct().ToArray();
        var indexes = distinct.Select(PgKeys.Index).ToArray();
        var depths = distinct.Select(key => (short)key.PartitionDepth).ToArray();
        var sessionIds = sessions.Distinct(StringComparer.Ordinal).ToArray();
        var indexesArgument = PgArgument.Typed(indexes, NpgsqlDbType.Array | NpgsqlDbType.Bytea);
        var depthsArgument = PgArgument.Typed(depths, NpgsqlDbType.Array | NpgsqlDbType.Smallint);
        var sessionIdsArgument = PgArgument.Typed(
            sessionIds,
            NpgsqlDbType.Array | NpgsqlDbType.Text
        );

        var (revisions, clocks, rows, sessionRows) = await transaction
            .FirstAsync(
                [
                    new PgStatement(SelectRevisionSql, keyspaceId),
                    new PgStatement(SelectClock),
                    new PgStatement(SelectRowsSql, keyspaceId, indexesArgument, depthsArgument),
                    new PgStatement(SelectSessionsSql, keyspaceId, sessionIdsArgument),
                ],
                async (results, token) =>
                {
                    var revisionRows = await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false);
                    var clockRows = await results
                        .ReadAsync(reader => reader.GetDateTime(0), token)
                        .ConfigureAwait(false);
                    var recordRows = await results
                        .ReadAsync(
                            reader => new PgRecordRow(
                                reader.GetFieldValue<byte[]>(0),
                                reader.GetInt16(1),
                                reader.GetString(2),
                                reader.GetFieldValue<byte[]>(3),
                                reader.GetInt64(4),
                                reader.IsDBNull(5)
                                    ? null
                                    : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                                reader.IsDBNull(6) ? null : reader.GetString(6),
                                reader.IsDBNull(7)
                                    ? null
                                    : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)
                            ),
                            token
                        )
                        .ConfigureAwait(false);
                    var sessionDeadlines = await results
                        .ReadAsync(
                            reader =>
                                (
                                    Id: reader.GetString(0),
                                    Deadline: DateTime.SpecifyKind(
                                        reader.GetDateTime(1),
                                        DateTimeKind.Utc
                                    )
                                ),
                            token
                        )
                        .ConfigureAwait(false);

                    return (revisionRows, clockRows, recordRows, sessionDeadlines);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revisions.Length == 0)
        {
            throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
        }

        var now = DateTime.SpecifyKind(clocks[0], DateTimeKind.Utc);
        var change = new PgKeyspaceChange(
            Settings.Schema,
            keyspaceId,
            revisions[0],
            now,
            Settings.LogRetention
        );

        var byIdentity = new Dictionary<(string Index, short Depth), PgRecordRow>();
        foreach (var row in rows)
        {
            byIdentity[(Convert.ToHexString(row.Index), row.Depth)] = row;
        }

        var found = ImmutableDictionary.CreateBuilder<CoordinationKey, PgRecordRow>();
        for (var position = 0; position < distinct.Length; position++)
        {
            var identity = (Convert.ToHexString(indexes[position]), depths[position]);
            if (byIdentity.TryGetValue(identity, out var row))
            {
                found[distinct[position]] = row;
            }
        }

        return new PgChangeStart(
            change,
            found.ToImmutable(),
            sessionRows.ToImmutableDictionary(
                row => row.Id,
                row => row.Deadline,
                StringComparer.Ordinal
            )
        );
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
        return transaction.CommitAsync(change.Statements(channel), cancellationToken);
    }
}
