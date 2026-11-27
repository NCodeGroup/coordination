// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// One subscriber of a log: the number it has been handed up to and the bounded queue of what it was handed.
/// </summary>
/// <typeparam name="TEntry">The type of the log's entries.</typeparam>
internal sealed class PgLogSubscriber<TEntry>
{
    /// <summary>
    /// The capacity of a subscriber's queue.
    /// </summary>
    public const int Capacity = 1_024;

    private readonly Channel<PgLogEvent<TEntry>> _queue = Channel.CreateBounded<PgLogEvent<TEntry>>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        }
    );

    private volatile bool _needsResync;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgLogSubscriber{TEntry}"/> class.
    /// </summary>
    /// <param name="cursor">The number of the last entry the subscriber has been handed.</param>
    /// <param name="resyncFirst">True when the subscriber starts with a resync.</param>
    public PgLogSubscriber(long cursor, bool resyncFirst)
    {
        Cursor = cursor;
        _needsResync = resyncFirst;
    }

    /// <summary>
    /// Gets or sets the number of the last entry or resync the subscriber has been handed.
    /// </summary>
    public long Cursor { get; set; }

    /// <summary>
    /// Gets or sets whether the subscriber's next event must be a resync: it was asked for, or an event didn't fit in
    /// its queue.
    /// </summary>
    public bool NeedsResync
    {
        get => _needsResync;
        set => _needsResync = value;
    }

    /// <summary>
    /// Gets the reader of the subscriber's queue.
    /// </summary>
    public ChannelReader<PgLogEvent<TEntry>> Reader => _queue.Reader;

    /// <summary>
    /// Adds an event to the subscriber's queue, or marks the subscriber as needing a resync when the queue is full.
    /// </summary>
    /// <param name="item">The event to add.</param>
    /// <returns>True when the event was added; otherwise, false.</returns>
    public bool TryWrite(PgLogEvent<TEntry> item)
    {
        if (_queue.Writer.TryWrite(item))
        {
            return true;
        }

        NeedsResync = true;
        return false;
    }
}
