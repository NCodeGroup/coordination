// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The notifications of the in-process provider: each topic is numbered from 1 for the provider's life, and each
/// subscription has a queue of its own.
/// </summary>
/// <remarks>
/// A subscription registers on its first read, starts with a resync at sequence 0, then yields the messages published
/// after it. When its queue overflows, the stream yields a resync at the last sequence it delivered.
/// </remarks>
internal class InProcessNotificationService : INotificationService
{
    /// <summary>
    /// The number of notifications a subscription's queue holds before it drops them.
    /// </summary>
    internal const int QueueCapacity = 1_024;

    /// <summary>
    /// Gets the lock that guards the sequences and the subscribers.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the last sequence published on each topic.
    /// </summary>
    private Dictionary<CoordinationKey, long> Sequences { get; } = [];

    /// <summary>
    /// Gets the queues of the subscriptions of each topic.
    /// </summary>
    private Dictionary<
        CoordinationKey,
        List<InProcessEventQueue<Notification>>
    > Subscribers { get; } = [];

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The payload is longer than the limit.</exception>
    public ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (payload.Length > CoordinationLimits.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"A payload has at most {CoordinationLimits.MaxPayloadLength} bytes.",
                nameof(payload)
            );
        }

        InProcessGuard.Check(options, cancellationToken);

        using var scope = Gate.EnterScope();

        var sequence = Sequences.GetValueOrDefault(topic) + 1;
        Sequences[topic] = sequence;

        if (Subscribers.TryGetValue(topic, out var queues))
        {
            var message = Notification.Message(topic, sequence, payload);

            foreach (var queue in queues)
            {
                queue.Post(message);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        InProcessGuard.Check(options, cancellationToken);

        return SubscribeCoreAsync(topic, cancellationToken);
    }

    /// <summary>
    /// Streams the notifications of a topic, registering the subscription on the first read and removing it when the
    /// stream ends.
    /// </summary>
    /// <param name="topic">The topic to stream.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of notifications.</returns>
    private async IAsyncEnumerable<Notification> SubscribeCoreAsync(
        CoordinationKey topic,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var queue = new InProcessEventQueue<Notification>(QueueCapacity);

        Start(topic, queue);

        try
        {
            yield return Notification.Resync(topic, 0);

            long delivered = 0;

            while (true)
            {
                var next = await queue.TakeAsync(cancellationToken).ConfigureAwait(false);

                if (next.Overflowed)
                {
                    yield return Notification.Resync(topic, delivered);
                }
                else
                {
                    delivered = next.Item.Sequence;

                    yield return next.Item;
                }
            }
        }
        finally
        {
            Stop(topic, queue);
        }
    }

    /// <summary>
    /// Registers a subscription's queue, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="topic">The topic the queue subscribes to.</param>
    /// <param name="queue">The queue to register.</param>
    private void Start(CoordinationKey topic, InProcessEventQueue<Notification> queue)
    {
        using var scope = Gate.EnterScope();

        if (!Subscribers.TryGetValue(topic, out var queues))
        {
            queues = [];
            Subscribers[topic] = queues;
        }

        queues.Add(queue);
    }

    /// <summary>
    /// Removes a subscription's queue, and its topic's list when it is empty, taking the lock in a method that does
    /// not await.
    /// </summary>
    /// <param name="topic">The topic the queue subscribed to.</param>
    /// <param name="queue">The queue to remove.</param>
    private void Stop(CoordinationKey topic, InProcessEventQueue<Notification> queue)
    {
        using var scope = Gate.EnterScope();

        if (!Subscribers.TryGetValue(topic, out var queues))
        {
            return;
        }

        queues.Remove(queue);

        if (queues.Count == 0)
        {
            Subscribers.Remove(topic);
        }
    }
}
