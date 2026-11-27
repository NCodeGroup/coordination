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

    /// <summary>The template of the statement that reads the rows of keys.</summary>
    private const string SelectRows = """
        SELECT r.idx, r.depth, r.key, r.value, r.revision, r.deadline, r.session_id
        FROM {schema}.records r
        WHERE r.keyspace_id = $1 AND (r.idx, r.depth) IN (SELECT * FROM unnest($2::bytea[], $3::smallint[]))
        """;

    /// <summary>Gets the seam to the server.</summary>
    public PgServer Server { get; } = server;

    /// <summary>Gets the keyspaces' ids.</summary>
    public PgKeyspaces Keyspaces { get; } = keyspaces;

    /// <summary>Gets the settings of the provider.</summary>
    public PgSettings Settings { get; } = settings;

    /// <summary>Gets the statement that locks the keyspace's row and reads its revision.</summary>
    private string SelectRevisionSql { get; } = settings.Schema.Apply(SelectRevision);

    /// <summary>Gets the statement that reads the rows of keys.</summary>
    private string SelectRowsSql { get; } = settings.Schema.Apply(SelectRows);

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
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The change, and the rows of the keys that exist.</returns>
    /// <exception cref="ArgumentNullException">The transaction or the keys are null.</exception>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    public async ValueTask<PgChangeStart> BeginAsync(
        PgTransaction transaction,
        long keyspaceId,
        IReadOnlyList<CoordinationKey> keys,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(keys);

        var distinct = keys.Distinct().ToArray();
        var indexes = distinct.Select(PgKeys.Index).ToArray();
        var depths = distinct.Select(key => (short)key.PartitionDepth).ToArray();

        var (revisions, clocks, rows) = await transaction
            .FirstAsync(
                [
                    new PgStatement(SelectRevisionSql, PgArgument.Of(keyspaceId)),
                    new PgStatement(SelectClock),
                    new PgStatement(
                        SelectRowsSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Typed(indexes, NpgsqlDbType.Array | NpgsqlDbType.Bytea),
                        PgArgument.Typed(depths, NpgsqlDbType.Array | NpgsqlDbType.Smallint)
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
                                reader.IsDBNull(6) ? null : reader.GetString(6)
                            ),
                            token
                        )
                        .ConfigureAwait(false);

                    return (revisionRows, clockRows, recordRows);
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

        return new PgChangeStart(change, found.ToImmutable());
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
