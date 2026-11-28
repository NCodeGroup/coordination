// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The record watches of the PostgreSQL provider: each subscribes to its keyspace's change log, which numbers every
/// change a watch can see, and yields the entries' items that its key or prefix covers. A subscriber that names a
/// revision the log still holds is replayed from it without a resync.
/// </summary>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="log">The reader of a keyspace's change log.</param>
/// <param name="hub">The owner of the provider's listening connection and logs' fanouts.</param>
/// <exception cref="ArgumentNullException">An argument is null.</exception>
internal sealed class PgRecordWatchService(PgKeyspaces keyspaces, PgChangeLog log, PgLogHub hub)
    : IRecordWatchService
{
    /// <summary>
    /// Turns one event of a log into the changes it holds that a watch covers.
    /// </summary>
    /// <param name="item">The event: a resync, or an entry.</param>
    /// <param name="keyspace">The keyspace that the watch covers.</param>
    /// <param name="matches">Tells whether the watch covers a change item.</param>
    /// <returns>
    /// A resync for a resync event; for an entry, a change for each covered item then for each covered key of an
    /// ended session.
    /// </returns>
    private static IEnumerable<RecordChange> ChangesOf(
        PgLogEvent<PgChange> item,
        Keyspace keyspace,
        Func<PgChangeItem, bool> matches
    )
    {
        if (item.IsResync)
        {
            var resyncRevision = new Revision(item.Current);
            yield return RecordChange.Resync(resyncRevision);
            yield break;
        }

        if (item.Entry is not { } entry)
        {
            yield break;
        }

        var revision = new Revision(entry.Number);

        foreach (var changed in entry.Items)
        {
            if (matches(changed))
            {
                yield return ToChange(keyspace, changed, revision);
            }
        }

        foreach (var ended in entry.EndedKeys)
        {
            if (matches(ended))
            {
                yield return ToDeletion(keyspace, ended, revision);
            }
        }
    }

    /// <summary>
    /// Turns an item of an entry into the change it logged.
    /// </summary>
    /// <param name="keyspace">The keyspace that the watch covers.</param>
    /// <param name="changed">The item.</param>
    /// <param name="revision">The entry's revision.</param>
    /// <returns>A write when the item holds a record written; otherwise, a deletion.</returns>
    private static RecordChange ToChange(Keyspace keyspace, PgChangeItem changed, Revision revision)
    {
        if (changed.Kind != PgChangeKinds.Written)
        {
            return ToDeletion(keyspace, changed, revision);
        }

        var key = PgRecordTable.DecodeKey(keyspace, changed.Key, changed.Depth);
        var record = new StoredRecord(key, changed.Value ?? [], revision);
        return RecordChange.Written(record);
    }

    /// <summary>
    /// Turns an item into the deletion of its key.
    /// </summary>
    /// <param name="keyspace">The keyspace that the watch covers.</param>
    /// <param name="deleted">The item.</param>
    /// <param name="revision">The entry's revision.</param>
    /// <returns>The deletion.</returns>
    private static RecordChange ToDeletion(
        Keyspace keyspace,
        PgChangeItem deleted,
        Revision revision
    )
    {
        var key = PgRecordTable.DecodeKey(keyspace, deleted.Key, deleted.Depth);
        return RecordChange.Deleted(key, revision);
    }

    /// <summary>
    /// Gets the resolver of keyspace ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; } =
        keyspaces ?? throw new ArgumentNullException(nameof(keyspaces));

    /// <summary>
    /// Gets the reader of a keyspace's change log.
    /// </summary>
    private PgChangeLog Log { get; } = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>
    /// Gets the owner of the provider's listening connection and logs' fanouts.
    /// </summary>
    private PgLogHub Hub { get; } = hub ?? throw new ArgumentNullException(nameof(hub));

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        return WatchCoreAsync(
            key.Keyspace,
            after,
            item => item.Depth == depth && item.Index.AsSpan().SequenceEqual(index),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(prefix);

        return WatchCoreAsync(
            prefix.Keyspace,
            after,
            item => item.Index.AsSpan().StartsWith(index),
            cancellationToken
        );
    }

    /// <summary>
    /// Streams the changes of a watch: the entries after the subscriber's revision, or a resync, then each covered
    /// item of each entry in order, and a resync after each gap.
    /// </summary>
    /// <param name="keyspace">The keyspace to watch.</param>
    /// <param name="after">The revision after which the subscriber wants the changes, or null for none.</param>
    /// <param name="matches">Tells whether the watch covers a change item.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of changes.</returns>
    private async IAsyncEnumerable<RecordChange> WatchCoreAsync(
        Keyspace keyspace,
        Revision? after,
        Func<PgChangeItem, bool> matches,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var keyspaceId = await Keyspaces
            .ResolveAsync(keyspace, cancellationToken)
            .ConfigureAwait(false);

        var logId = new PgLogId(keyspaceId, null);
        var subscriber = await Hub.SubscribeAsync(
                logId,
                () => CreateFanout(keyspaceId),
                after?.Value,
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            await foreach (
                var item in subscriber.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                var changes = ChangesOf(item, keyspace, matches);
                foreach (var change in changes)
                {
                    yield return change;
                }
            }
        }
        finally
        {
            await Hub.UnsubscribeAsync(logId, subscriber, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the fanout that reads a keyspace's change log.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <returns>A fanout that reads the keyspace's change log.</returns>
    private PgLogFanout<PgChange> CreateFanout(long keyspaceId)
    {
        return new PgLogFanout<PgChange>(
            (cursor, limit, readToken) => Log.ReadAsync(keyspaceId, cursor, limit, readToken)
        );
    }
}
