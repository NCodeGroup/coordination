// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Notifications whose publishes fail while the connection is down and whose subscriptions resync after a drop.
/// </summary>
/// <param name="inner">The notifications to decorate.</param>
/// <param name="connection">The connection that a test drops.</param>
internal sealed class DroppingNotificationService(
    INotificationService inner,
    DroppingConnection connection
) : INotificationService
{
    /// <summary>
    /// Gets the notifications that this decorator calls over the connection.
    /// </summary>
    private INotificationService Inner { get; } = inner;

    /// <summary>
    /// Gets the connection that a test drops.
    /// </summary>
    private DroppingConnection Connection { get; } = connection;

    /// <inheritdoc />
    public ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) =>
        Connection.CallAsync(
            token => Inner.PublishAsync(topic, payload, options, token),
            cancellationToken
        );

    /// <inheritdoc />
    public IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        var stream = Inner.SubscribeAsync(topic, options, cancellationToken);

        return ResyncAsync(topic, stream, cancellationToken);
    }

    /// <summary>
    /// Reads a stream and replaces the item that a drop loses with a resync.
    /// </summary>
    /// <param name="topic">The topic of the stream.</param>
    /// <param name="stream">The stream of the inner provider.</param>
    /// <param name="cancellationToken">A token that cancels the reading.</param>
    /// <returns>The notifications of the stream, with a resync in place of each one a drop lost.</returns>
    private async IAsyncEnumerable<Notification> ResyncAsync(
        CoordinationKey topic,
        IAsyncEnumerable<Notification> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var subscription = Connection.Subscribe();

        try
        {
            long delivered = 0;

            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                if (subscription.TakeDrop() || item.IsResync)
                {
                    // A drop loses that item, missed while the connection was down, and every resync carries
                    // the last sequence this stream delivered.
                    yield return Notification.Resync(topic, delivered);
                }
                else
                {
                    delivered = item.Sequence;
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
