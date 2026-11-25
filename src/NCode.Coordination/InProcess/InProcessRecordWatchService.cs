// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The record watches of the in-process provider: each watch has a queue of its own, fed by the record table.
/// </summary>
/// <param name="table">The record table that the watches are registered with.</param>
/// <remarks>
/// A watch registers on its first read and removes itself when its stream ends. When a watch's queue overflows, the
/// stream yields a resync at the revision of the newest change that was dropped.
/// </remarks>
internal class InProcessRecordWatchService(InProcessRecordTable table) : IRecordWatchService
{
    /// <summary>
    /// Gets the record table that the watches are registered with.
    /// </summary>
    private InProcessRecordTable Table { get; } = table;

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        return WatchCoreAsync(
            new InProcessRecordWatcher(key, isPrefix: false),
            after,
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
        InProcessGuard.Check(options, cancellationToken);

        return WatchCoreAsync(
            new InProcessRecordWatcher(prefix, isPrefix: true),
            after,
            cancellationToken
        );
    }

    /// <summary>
    /// Streams the changes of a watch, registering it on the first read and removing it when the stream ends.
    /// </summary>
    /// <param name="watcher">The watch to stream.</param>
    /// <param name="after">The revision after which the subscriber wants the changes, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of changes.</returns>
    private async IAsyncEnumerable<RecordChange> WatchCoreAsync(
        InProcessRecordWatcher watcher,
        Revision? after,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Start(watcher, after);

        try
        {
            while (true)
            {
                var next = await watcher.Queue.TakeAsync(cancellationToken).ConfigureAwait(false);

                yield return next.Overflowed ? RecordChange.Resync(next.Item.Revision) : next.Item;
            }
        }
        finally
        {
            Stop(watcher);
        }
    }

    /// <summary>
    /// Registers a watch with the record table, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="watcher">The watch to register.</param>
    /// <param name="after">The revision after which the subscriber wants the changes, or null for none.</param>
    private void Start(InProcessRecordWatcher watcher, Revision? after)
    {
        using (Table.Enter())
        {
            Table.AddWatcher(watcher, after);
        }
    }

    /// <summary>
    /// Removes a watch from the record table, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="watcher">The watch to remove.</param>
    private void Stop(InProcessRecordWatcher watcher)
    {
        using (Table.Enter())
        {
            Table.RemoveWatcher(watcher);
        }
    }
}
