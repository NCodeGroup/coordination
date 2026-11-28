// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Reads a keyspace's change log: its highest removed revision, its current revision and the entries after a
/// revision, with the keys that each session's end covers, all in one snapshot.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="schema">The schema that holds the tables.</param>
/// <exception cref="ArgumentNullException">An argument is null.</exception>
internal sealed class PgChangeLog(PgServer server, PgSchemaName schema)
{
    /// <summary>The kind that a change item holds for a record that was deleted.</summary>
    private const short DeletedKind = 2;

    /// <summary>The most keys that one page of an ended session's keys holds.</summary>
    private const int EndedPageSize = 1_000;

    /// <summary>
    /// The statement that reads the keyspace's highest removed revision and current revision. Its parameter is the
    /// keyspace id.
    /// </summary>
    private const string HeaderSql = """
        SELECT log_removed, revision FROM {schema}.keyspaces WHERE id = $1
        """;

    /// <summary>
    /// The statement that reads the entries after a revision with their items. Its parameters are the keyspace id,
    /// the revision to read after and the most entries to return.
    /// </summary>
    private const string EntriesSql = """
        SELECT c.revision, c.ended_session, c.group_idx, c.group_depth, i.kind, i.idx, i.depth, i.key, i.value
        FROM (SELECT * FROM {schema}.changes WHERE keyspace_id = $1 AND revision > $2 ORDER BY revision LIMIT $3) c
        LEFT JOIN {schema}.change_items i ON i.keyspace_id = c.keyspace_id AND i.revision = c.revision
        ORDER BY c.revision, i.ordinal
        """;

    /// <summary>
    /// The statement that reads a page of the keys still bound to an ended session, which are those of the records
    /// that name it and those that later entries replaced. Its parameters are the keyspace id, the session's id, the
    /// revision of the entry that ended it, and the index value and depth to read after.
    /// </summary>
    private const string EndedKeysSql = """
        SELECT idx, depth, key FROM (
          SELECT r.idx, r.depth, r.key FROM {schema}.records r WHERE r.keyspace_id = $1 AND r.session_id = $2
          UNION
          SELECT i.idx, i.depth, i.key FROM {schema}.change_items i
          WHERE i.keyspace_id = $1 AND i.replaced_session = $2 AND i.revision > $3) b
        WHERE (idx, depth) > ($4, $5)
        ORDER BY idx, depth
        LIMIT 1000
        """;

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the statement that reads the keyspace's highest removed and current revisions.
    /// </summary>
    private string Header { get; } = schema.Apply(HeaderSql);

    /// <summary>
    /// Gets the statement that reads the entries after a revision.
    /// </summary>
    private string Entries { get; } = schema.Apply(EntriesSql);

    /// <summary>
    /// Gets the statement that reads a page of an ended session's keys.
    /// </summary>
    private string EndedKeys { get; } = schema.Apply(EndedKeysSql);

    /// <summary>
    /// Reads a keyspace's change log after a revision, in one read-only snapshot.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="after">The revision to read after.</param>
    /// <param name="limit">The most entries to return.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>
    /// The highest removed revision, the current revision and the entries after <paramref name="after"/>, in order;
    /// an entry that ended a session holds the keys still bound to it.
    /// </returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    public async ValueTask<PgLogRead<PgChange>> ReadAsync(
        long keyspaceId,
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        return await Server
            .TransactAsync(
                PgIsolation.RepeatableReadReadOnly,
                async (transaction, token) =>
                {
                    var (header, entries) = await transaction
                        .FirstAsync(
                            [
                                new PgStatement(Header, keyspaceId),
                                new PgStatement(Entries, keyspaceId, after, (long)limit),
                            ],
                            async (results, readToken) =>
                            {
                                var headerRows = await results
                                    .ReadAsync(
                                        reader =>
                                            (
                                                Removed: reader.GetInt64(0),
                                                Current: reader.GetInt64(1)
                                            ),
                                        readToken
                                    )
                                    .ConfigureAwait(false);

                                var itemRows = await results
                                    .ReadAsync(
                                        reader =>
                                            (
                                                Revision: reader.GetInt64(0),
                                                EndedSession: reader.IsDBNull(1)
                                                    ? null
                                                    : reader.GetString(1),
                                                GroupIndex: reader.IsDBNull(2)
                                                    ? null
                                                    : reader.GetFieldValue<byte[]>(2),
                                                GroupDepth: reader.IsDBNull(3)
                                                    ? (short?)null
                                                    : reader.GetInt16(3),
                                                Item: reader.IsDBNull(4)
                                                    ? null
                                                    : new PgChangeItem(
                                                        reader.GetInt16(4),
                                                        reader.GetFieldValue<byte[]>(5),
                                                        reader.GetInt16(6),
                                                        reader.GetString(7),
                                                        reader.IsDBNull(8)
                                                            ? null
                                                            : reader.GetFieldValue<byte[]>(8)
                                                    )
                                            ),
                                        readToken
                                    )
                                    .ConfigureAwait(false);

                                return (headerRows, itemRows);
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    var changes = new List<PgChange>();
                    var items = ImmutableArray.CreateBuilder<PgChangeItem>();
                    for (var position = 0; position < entries.Length; position++)
                    {
                        var row = entries[position];
                        if (row.Item is { } item)
                        {
                            items.Add(item);
                        }

                        if (
                            position + 1 < entries.Length
                            && entries[position + 1].Revision == row.Revision
                        )
                        {
                            continue;
                        }

                        changes.Add(
                            new PgChange(
                                row.Revision,
                                row.EndedSession,
                                row.GroupIndex,
                                row.GroupDepth,
                                items.ToImmutable(),
                                []
                            )
                        );
                        items.Clear();
                    }

                    var result = ImmutableArray.CreateBuilder<PgChange>(changes.Count);
                    foreach (var change in changes)
                    {
                        result.Add(
                            change.EndedSession is { } session
                                ? change with
                                {
                                    EndedKeys = await ReadEndedKeysAsync(
                                            transaction,
                                            keyspaceId,
                                            session,
                                            change.Number,
                                            token
                                        )
                                        .ConfigureAwait(false),
                                }
                                : change
                        );
                    }

                    return header.Length == 0
                        ? new PgLogRead<PgChange>(0, 0, result.ToImmutable())
                        : new PgLogRead<PgChange>(
                            header[0].Removed,
                            header[0].Current,
                            result.ToImmutable()
                        );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads, in pages, the keys still bound to a session that an entry ended.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="session">The ended session's id.</param>
    /// <param name="revision">The revision of the entry that ended the session.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The keys, as deleted items in index order.</returns>
    private async ValueTask<ImmutableArray<PgChangeItem>> ReadEndedKeysAsync(
        PgTransaction transaction,
        long keyspaceId,
        string session,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var keys = ImmutableArray.CreateBuilder<PgChangeItem>();
        var lastIndex = Array.Empty<byte>();
        short lastDepth = -1;

        while (true)
        {
            var lastIndexArgument = PgArgument.Typed(lastIndex, NpgsqlDbType.Bytea);
            var lastDepthArgument = PgArgument.Typed(lastDepth, NpgsqlDbType.Smallint);
            var page = await transaction
                .NextAsync(
                    [
                        new PgStatement(
                            EndedKeys,
                            keyspaceId,
                            session,
                            revision,
                            lastIndexArgument,
                            lastDepthArgument
                        ),
                    ],
                    async (results, readToken) =>
                        await results
                            .ReadAsync(
                                reader => new PgChangeItem(
                                    DeletedKind,
                                    reader.GetFieldValue<byte[]>(0),
                                    reader.GetInt16(1),
                                    reader.GetString(2),
                                    null
                                ),
                                readToken
                            )
                            .ConfigureAwait(false),
                    cancellationToken
                )
                .ConfigureAwait(false);

            keys.AddRange(page);
            if (page.Length < EndedPageSize)
            {
                return keys.ToImmutable();
            }

            lastIndex = page[^1].Index;
            lastDepth = page[^1].Depth;
        }
    }
}
