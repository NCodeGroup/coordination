// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// One subscriber of a log: the number it has been handed up to and the bounded queue of what it was handed.
/// </summary>
/// <typeparam name="TEntry">The type of the log's entries.</typeparam>
/// <param name="cursor">The number of the last entry the subscriber has been handed.</param>
/// <param name="resyncFirst">True when the subscriber starts with a resync.</param>
internal sealed class PgLogSubscriber<TEntry>(long cursor, bool resyncFirst)
{
    /// <summary>
    /// The capacity of a subscriber's queue.
    /// </summary>
    public const int Capacity = 1_024;

    /// <summary>
    /// Creates the bounded queue of a subscriber.
    /// </summary>
    /// <returns>The queue, with one reader and one writer.</returns>
    private static Channel<PgLogEvent<TEntry>> CreateQueue()
    {
        var options = new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        };

        return Channel.CreateBounded<PgLogEvent<TEntry>>(options);
    }

    /// <summary>
    /// Whether the subscriber's next event must be a resync; read by <see cref="NeedsResync"/>.
    /// </summary>
    private bool _needsResync = resyncFirst;

    /// <summary>
    /// Gets the bounded queue of what the subscriber was handed.
    /// </summary>
    private Channel<PgLogEvent<TEntry>> Queue { get; } = CreateQueue();

    /// <summary>
    /// Gets or sets the number of the last entry or resync the subscriber has been handed.
    /// </summary>
    public long Cursor { get; set; } = cursor;

    /// <summary>
    /// Gets or sets whether the subscriber's next event must be a resync: it was asked for, or an event didn't fit in
    /// its queue.
    /// </summary>
    public bool NeedsResync
    {
        get => Volatile.Read(ref _needsResync);
        set => Volatile.Write(ref _needsResync, value);
    }

    /// <summary>
    /// Gets the reader of the subscriber's queue.
    /// </summary>
    public ChannelReader<PgLogEvent<TEntry>> Reader => Queue.Reader;

    /// <summary>
    /// Adds an event to the subscriber's queue, or marks the subscriber as needing a resync when the queue is full.
    /// </summary>
    /// <param name="item">The event to add.</param>
    /// <returns>True when the event was added; otherwise, false.</returns>
    public bool TryWrite(PgLogEvent<TEntry> item)
    {
        if (Queue.Writer.TryWrite(item))
        {
            return true;
        }

        NeedsResync = true;
        return false;
    }
}
