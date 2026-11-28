// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Reads a keyspace's change log: its highest removed revision, its current revision and the entries after a
/// revision, with the keys that each session's end covers, all in one snapshot.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="schema">The schema that holds the tables.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal sealed class PgChangeLog(PgServer server, PgSchemaName schema)
{
    /// <summary>
    /// The most keys that one page of an ended session's keys holds.
    /// </summary>
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
    /// revision of the entry that ended it, the index value and depth to read after, and the page size. Each branch
    /// reads from an index in key order and stops at one page, so a page costs a page, not every remaining key.
    /// </summary>
    private const string EndedKeysSql = """
        SELECT idx, depth, key FROM (
          (SELECT r.idx, r.depth, r.key FROM {schema}.records r
           WHERE r.keyspace_id = $1 AND r.session_id = $2 AND (r.idx, r.depth) > ($4, $5)
           ORDER BY r.idx, r.depth LIMIT $6)
          UNION
          (SELECT DISTINCT i.idx, i.depth, i.key FROM {schema}.change_items i
           WHERE i.keyspace_id = $1 AND i.replaced_session = $2 AND i.revision > $3 AND (i.idx, i.depth) > ($4, $5)
           ORDER BY i.idx, i.depth LIMIT $6)) b
        ORDER BY idx, depth
        LIMIT $6
        """;

    /// <summary>
    /// Reads the results of the first batch: the header's row, then the entries' rows.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The header's rows and the entries' rows.</returns>
    private static async ValueTask<(
        ImmutableArray<(long Removed, long Current)> Header,
        ImmutableArray<PgChangeRow> Entries
    )> ReadHeaderAndEntryRowsAsync(PgResults results, CancellationToken cancellationToken)
    {
        var headerRows = await results
            .ReadAsync(ReadHeaderRow, cancellationToken)
            .ConfigureAwait(false);

        var entryRows = await results
            .ReadAsync(ReadChangeRow, cancellationToken)
            .ConfigureAwait(false);

        return (headerRows, entryRows);
    }

    /// <summary>
    /// Reads the keyspace's highest removed revision and current revision from the reader's current row.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row of the header statement.</param>
    /// <returns>The highest removed revision and the current revision.</returns>
    private static (long Removed, long Current) ReadHeaderRow(NpgsqlDataReader reader)
    {
        var removed = reader.GetInt64(0);
        var current = reader.GetInt64(1);
        return (removed, current);
    }

    /// <summary>
    /// Reads an entry's columns and its item from the reader's current row.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row of the entries statement.</param>
    /// <returns>The row.</returns>
    private static PgChangeRow ReadChangeRow(NpgsqlDataReader reader)
    {
        var revision = reader.GetInt64(0);
        var endedSession = reader.IsDBNull(1) ? null : reader.GetString(1);
        var groupIndex = reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2);
        short? groupDepth = reader.IsDBNull(3) ? null : reader.GetInt16(3);
        var item = ReadChangeItem(reader);
        return new PgChangeRow(revision, endedSession, groupIndex, groupDepth, item);
    }

    /// <summary>
    /// Reads the item that the reader's current row of the entries statement carries.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row of the entries statement.</param>
    /// <returns>The item, or null when the entry has no items.</returns>
    private static PgChangeItem? ReadChangeItem(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(4))
        {
            return null;
        }

        var kind = reader.GetInt16(4);
        var index = reader.GetFieldValue<byte[]>(5);
        var depth = reader.GetInt16(6);
        var key = reader.GetString(7);
        var value = reader.IsDBNull(8) ? null : reader.GetFieldValue<byte[]>(8);
        return new PgChangeItem(kind, index, depth, key, value);
    }

    /// <summary>
    /// Groups the rows of the entries statement into entries, each with its items.
    /// </summary>
    /// <param name="rows">The rows, in revision order.</param>
    /// <returns>The entries, in revision order, without the keys of ended sessions.</returns>
    private static ImmutableArray<PgChange> GroupEntries(ImmutableArray<PgChangeRow> rows)
    {
        var changes = ImmutableArray.CreateBuilder<PgChange>(rows.Length);
        var items = ImmutableArray.CreateBuilder<PgChangeItem>(rows.Length);
        for (var position = 0; position < rows.Length; position++)
        {
            var row = rows[position];
            if (row.Item is { } item)
            {
                items.Add(item);
            }

            if (!EndsEntry(rows, position))
            {
                continue;
            }

            var entryItems = items.ToImmutable();
            var change = new PgChange(
                row.Revision,
                row.EndedSession,
                row.GroupIndex,
                row.GroupDepth,
                entryItems,
                []
            );
            changes.Add(change);
            items.Clear();
        }

        return changes.ToImmutable();
    }

    /// <summary>
    /// Tells whether a row is the last one of its entry.
    /// </summary>
    /// <param name="rows">The rows, in revision order.</param>
    /// <param name="position">The position of the row.</param>
    /// <returns>True when no row follows with the same revision; otherwise, false.</returns>
    private static bool EndsEntry(ImmutableArray<PgChangeRow> rows, int position)
    {
        var nextPosition = position + 1;
        return nextPosition >= rows.Length
            || rows[nextPosition].Revision != rows[position].Revision;
    }

    /// <summary>
    /// Builds the result of a read from the header's rows and the entries.
    /// </summary>
    /// <param name="header">The header's rows, which are empty when the keyspace's row is gone.</param>
    /// <param name="changes">The entries, with the keys of ended sessions.</param>
    /// <returns>The highest removed revision, the current revision and the entries.</returns>
    private static PgLogRead<PgChange> ToLogRead(
        ImmutableArray<(long Removed, long Current)> header,
        ImmutableArray<PgChange> changes
    )
    {
        if (header.Length == 0)
        {
            return new PgLogRead<PgChange>(0, 0, changes);
        }

        var row = header[0];
        return new PgLogRead<PgChange>(row.Removed, row.Current, changes);
    }

    /// <summary>
    /// Reads the results of a page of an ended session's keys.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The page's keys, as deleted items.</returns>
    private static ValueTask<ImmutableArray<PgChangeItem>> ReadEndedKeyPageAsync(
        PgResults results,
        CancellationToken cancellationToken
    ) => results.ReadAsync(ReadEndedKeyRow, cancellationToken);

    /// <summary>
    /// Reads a deleted item from the reader's current row of the ended keys statement.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row of the ended keys statement.</param>
    /// <returns>The deleted item.</returns>
    private static PgChangeItem ReadEndedKeyRow(NpgsqlDataReader reader)
    {
        var index = reader.GetFieldValue<byte[]>(0);
        var depth = reader.GetInt16(1);
        var key = reader.GetString(2);
        return new PgChangeItem(PgChangeKinds.Deleted, index, depth, key, null);
    }

    /// <summary>
    /// Lists the cursors that start the first page of each session that an entry ended.
    /// </summary>
    /// <param name="changes">The entries, in revision order.</param>
    /// <returns>One cursor for each entry that ended a session, in the entries' order.</returns>
    private static ImmutableArray<PgEndedKeysCursor> EndedCursors(IReadOnlyList<PgChange> changes)
    {
        var cursors = ImmutableArray.CreateBuilder<PgEndedKeysCursor>();
        foreach (var change in changes)
        {
            if (change.EndedSession is not { } session)
            {
                continue;
            }

            var cursor = PgEndedKeysCursor.First(session, change.Number);
            cursors.Add(cursor);
        }

        return cursors.ToImmutable();
    }

    /// <summary>
    /// Reads the results of a batch that holds one page of ended keys for each cursor.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="count">The number of pages, which is that of the batch's statements.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The pages, in the order of the statements.</returns>
    private static async ValueTask<ImmutableArray<ImmutableArray<PgChangeItem>>> ReadPagesAsync(
        PgResults results,
        int count,
        CancellationToken cancellationToken
    )
    {
        var pages = ImmutableArray.CreateBuilder<ImmutableArray<PgChangeItem>>(count);
        for (var number = 0; number < count; number++)
        {
            var page = await ReadEndedKeyPageAsync(results, cancellationToken)
                .ConfigureAwait(false);

            pages.Add(page);
        }

        return pages.ToImmutable();
    }

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
                (transaction, token) =>
                    ReadSnapshotAsync(transaction, keyspaceId, after, limit, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the header, the entries and the keys of the ended sessions inside the snapshot.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="after">The revision to read after.</param>
    /// <param name="limit">The most entries to return.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The highest removed revision, the current revision and the entries.</returns>
    private async ValueTask<PgLogRead<PgChange>> ReadSnapshotAsync(
        PgTransaction transaction,
        long keyspaceId,
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var (header, entryRows) = await ReadFirstBatchAsync(
                transaction,
                keyspaceId,
                after,
                limit,
                cancellationToken
            )
            .ConfigureAwait(false);

        var changes = GroupEntries(entryRows);
        var completed = await AddEndedKeysAsync(transaction, keyspaceId, changes, cancellationToken)
            .ConfigureAwait(false);

        return ToLogRead(header, completed);
    }

    /// <summary>
    /// Runs the first batch of the snapshot: the header's statement and the entries' statement.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="after">The revision to read after.</param>
    /// <param name="limit">The most entries to return.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The header's rows and the entries' rows.</returns>
    private async ValueTask<(
        ImmutableArray<(long Removed, long Current)> Header,
        ImmutableArray<PgChangeRow> Entries
    )> ReadFirstBatchAsync(
        PgTransaction transaction,
        long keyspaceId,
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var headerStatement = new PgStatement(Header, keyspaceId);
        var entriesStatement = new PgStatement(Entries, keyspaceId, after, (long)limit);
        IReadOnlyList<PgStatement> statements = [headerStatement, entriesStatement];

        return await transaction
            .FirstAsync(statements, ReadHeaderAndEntryRowsAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds to each entry that ended a session the keys still bound to the session.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="entries">The entries, in revision order.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The entries, in revision order, each with its ended keys.</returns>
    private async ValueTask<ImmutableArray<PgChange>> AddEndedKeysAsync(
        PgTransaction transaction,
        long keyspaceId,
        IReadOnlyList<PgChange> entries,
        CancellationToken cancellationToken
    )
    {
        var cursors = EndedCursors(entries);
        if (cursors.IsEmpty)
        {
            return ImmutableArray.CreateRange(entries);
        }

        var pages = await ReadFirstPagesAsync(transaction, keyspaceId, cursors, cancellationToken)
            .ConfigureAwait(false);

        return await CompleteEntriesAsync(
                transaction,
                keyspaceId,
                entries,
                cursors,
                pages,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the keys of each ended session on the entry that ended it, reading the pages after a full first page.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="entries">The entries, in revision order.</param>
    /// <param name="cursors">The cursors that started the first pages, one for each entry that ended a session.</param>
    /// <param name="pages">The first pages, in the order of the cursors.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The entries, in revision order, each with its ended keys.</returns>
    private async ValueTask<ImmutableArray<PgChange>> CompleteEntriesAsync(
        PgTransaction transaction,
        long keyspaceId,
        IReadOnlyList<PgChange> entries,
        IReadOnlyList<PgEndedKeysCursor> cursors,
        ImmutableArray<ImmutableArray<PgChangeItem>> pages,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<PgChange>(entries.Count);
        var position = 0;
        foreach (var entry in entries)
        {
            if (entry.EndedSession is null)
            {
                result.Add(entry);
                continue;
            }

            var keys = await CompleteKeysAsync(
                    transaction,
                    keyspaceId,
                    cursors[position],
                    pages[position],
                    cancellationToken
                )
                .ConfigureAwait(false);

            result.Add(entry with { EndedKeys = keys });
            position++;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Completes a session's keys: the first page, and the pages after it when the first one was full.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cursor">The cursor that started the first page.</param>
    /// <param name="firstPage">The first page of the session's keys.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The keys, as deleted items in index order.</returns>
    private async ValueTask<ImmutableArray<PgChangeItem>> CompleteKeysAsync(
        PgTransaction transaction,
        long keyspaceId,
        PgEndedKeysCursor cursor,
        ImmutableArray<PgChangeItem> firstPage,
        CancellationToken cancellationToken
    )
    {
        if (firstPage.Length < EndedPageSize)
        {
            return firstPage;
        }

        var next = cursor.After(firstPage[^1]);
        var rest = await ReadRemainingKeysAsync(transaction, keyspaceId, next, cancellationToken)
            .ConfigureAwait(false);

        return firstPage.AddRange(rest);
    }

    /// <summary>
    /// Builds the statement that reads the page of an ended session's keys that a cursor starts.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cursor">The cursor that starts the page.</param>
    /// <returns>The statement.</returns>
    private PgStatement EndedKeysStatement(long keyspaceId, PgEndedKeysCursor cursor)
    {
        var afterIndex = PgArgument.Typed(cursor.AfterIndex, NpgsqlDbType.Bytea);
        var afterDepth = PgArgument.Typed(cursor.AfterDepth, NpgsqlDbType.Smallint);
        var pageSize = PgArgument.Typed(EndedPageSize, NpgsqlDbType.Integer);
        return new PgStatement(
            EndedKeys,
            keyspaceId,
            cursor.Session,
            cursor.Revision,
            afterIndex,
            afterDepth,
            pageSize
        );
    }

    /// <summary>
    /// Reads the first page of every ended session's keys in one batch.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cursors">The cursors that start the first pages.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The pages, in the order of the cursors.</returns>
    private async ValueTask<ImmutableArray<ImmutableArray<PgChangeItem>>> ReadFirstPagesAsync(
        PgTransaction transaction,
        long keyspaceId,
        IReadOnlyList<PgEndedKeysCursor> cursors,
        CancellationToken cancellationToken
    )
    {
        var statements = new List<PgStatement>(cursors.Count);
        foreach (var cursor in cursors)
        {
            var statement = EndedKeysStatement(keyspaceId, cursor);
            statements.Add(statement);
        }

        var count = cursors.Count;
        return await transaction
            .NextAsync(
                statements,
                (results, token) => ReadPagesAsync(results, count, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads, in pages, the keys still bound to a session that an entry ended, from a cursor on.
    /// </summary>
    /// <param name="transaction">The transaction that holds the snapshot.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cursor">The cursor that starts the first page to read.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The keys after the cursor, as deleted items in index order.</returns>
    private async ValueTask<ImmutableArray<PgChangeItem>> ReadRemainingKeysAsync(
        PgTransaction transaction,
        long keyspaceId,
        PgEndedKeysCursor cursor,
        CancellationToken cancellationToken
    )
    {
        var keys = ImmutableArray.CreateBuilder<PgChangeItem>();

        while (true)
        {
            var statement = EndedKeysStatement(keyspaceId, cursor);
            IReadOnlyList<PgStatement> statements = [statement];
            var page = await transaction
                .NextAsync(statements, ReadEndedKeyPageAsync, cancellationToken)
                .ConfigureAwait(false);

            keys.AddRange(page);
            if (page.Length < EndedPageSize)
            {
                return keys.ToImmutable();
            }

            cursor = cursor.After(page[^1]);
        }
    }
}
