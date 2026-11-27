// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Record watches whose streams resync after a drop of the connection.
/// </summary>
/// <param name="inner">The record watches to decorate.</param>
/// <param name="connection">The connection that a test drops.</param>
internal sealed class DroppingRecordWatchService(
    IRecordWatchService inner,
    DroppingConnection connection
) : IRecordWatchService
{
    /// <summary>
    /// Gets the record watches that this decorator calls over the connection.
    /// </summary>
    private IRecordWatchService Inner { get; } = inner;

    /// <summary>
    /// Gets the connection that a test drops.
    /// </summary>
    private DroppingConnection Connection { get; } = connection;

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        var stream = Inner.WatchKeyAsync(key, after, options, cancellationToken);

        return ResyncAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        var stream = Inner.WatchPrefixAsync(prefix, after, options, cancellationToken);

        return ResyncAsync(stream, cancellationToken);
    }

    /// <summary>
    /// Reads a stream and replaces the change that a drop loses with a resync at that change's revision.
    /// </summary>
    /// <param name="stream">The stream of the inner provider.</param>
    /// <param name="cancellationToken">A token that cancels the reading.</param>
    /// <returns>The changes of the stream, with a resync in place of each one a drop lost.</returns>
    private async IAsyncEnumerable<RecordChange> ResyncAsync(
        IAsyncEnumerable<RecordChange> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var subscription = Connection.Subscribe();

        try
        {
            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                if (subscription.TakeDrop() && !item.IsResync)
                {
                    // A drop loses that change, missed while the connection was down; a watch that reconnects
                    // resyncs at the revision it reads then, at or after the change it missed.
                    yield return RecordChange.Resync(item.Revision);
                }
                else
                {
                    yield return item;
                }
            }
        }
        finally
        {
            Connection.Unsubscribe(subscription);
        }
    }
}
