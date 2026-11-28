// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// Reads one log, a keyspace's change log or a topic's message log, once for all of its subscribers and hands each
/// the entries after its cursor in order, or a resync when it may have missed one.
/// </summary>
/// <typeparam name="TEntry">The type of the log's entries.</typeparam>
/// <param name="read">
/// Reads the log after a number: the number to read after, the most entries to return and the token that cancels the
/// read.
/// </param>
/// <exception cref="ArgumentNullException">The read function is null.</exception>
internal sealed class PgLogFanout<TEntry>(
    Func<long, int, CancellationToken, ValueTask<PgLogRead<TEntry>>> read
) : IPgLogPump
    where TEntry : IPgLogEntry
{
    /// <summary>
    /// The most entries that one read returns.
    /// </summary>
    public const int PageSize = 256;

    /// <summary>
    /// Gets the lowest cursor among the subscribers that don't need a resync.
    /// </summary>
    /// <param name="subscribers">The subscribers.</param>
    /// <returns>The lowest cursor, or <see cref="long.MaxValue"/> when every subscriber needs a resync.</returns>
    private static long Lowest(PgLogSubscriber<TEntry>[] subscribers)
    {
        var lowest = long.MaxValue;
        foreach (var subscriber in subscribers)
        {
            if (!subscriber.NeedsResync && subscriber.Cursor < lowest)
            {
                lowest = subscriber.Cursor;
            }
        }

        return lowest;
    }

    /// <summary>
    /// Hands a subscriber what a read holds: a resync when it needs one or the log no longer holds what it needs,
    /// otherwise each entry above its cursor in order, resyncing at the first that isn't the cursor plus one.
    /// </summary>
    /// <param name="subscriber">The subscriber.</param>
    /// <param name="page">What the read holds.</param>
    private static void Deliver(PgLogSubscriber<TEntry> subscriber, PgLogRead<TEntry> page)
    {
        if (
            subscriber.NeedsResync
            || page.Current < subscriber.Cursor
            || subscriber.Cursor < page.Removed
        )
        {
            Resync(subscriber, page.Current);
            return;
        }

        foreach (var entry in page.Entries)
        {
            var number = entry.Number;
            if (number <= subscriber.Cursor)
            {
                continue;
            }

            if (number != subscriber.Cursor + 1)
            {
                Resync(subscriber, page.Current);
                return;
            }

            var entryEvent = PgLogEvent<TEntry>.ForEntry(entry, number);
            if (!subscriber.TryWrite(entryEvent))
            {
                return;
            }

            subscriber.Cursor = number;
        }
    }

    /// <summary>
    /// Moves a subscriber to the log's current number and hands it a resync; when its queue is full, it stays marked as
    /// needing one.
    /// </summary>
    /// <param name="subscriber">The subscriber.</param>
    /// <param name="current">The log's current number.</param>
    private static void Resync(PgLogSubscriber<TEntry> subscriber, long current)
    {
        var resyncEvent = PgLogEvent<TEntry>.ForResync(current);
        subscriber.Cursor = current;
        subscriber.NeedsResync = !subscriber.TryWrite(resyncEvent);
    }

    /// <summary>
    /// Gets the lock that guards the subscribers.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the subscribers, guarded by <see cref="Gate" />.
    /// </summary>
    private List<PgLogSubscriber<TEntry>> Subscribers { get; } = [];

    // Never disposed: a SemaphoreSlim whose AvailableWaitHandle is never read holds nothing to free, and a pump may
    // still be releasing it when the log's last subscriber leaves.
    /// <summary>
    /// Gets the gate that serializes the pumps.
    /// </summary>
    private SemaphoreSlim Pumping { get; } = new(1, 1);

    /// <summary>
    /// Gets the function that reads the log after a number.
    /// </summary>
    private Func<long, int, CancellationToken, ValueTask<PgLogRead<TEntry>>> Read { get; } =
        read ?? throw new ArgumentNullException(nameof(read));

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (Gate)
            {
                return Subscribers.Count;
            }
        }
    }

    /// <summary>
    /// Adds a subscriber.
    /// </summary>
    /// <param name="cursor">The number after which the subscriber wants the log's entries.</param>
    /// <param name="resyncFirst">True when the subscriber starts with a resync at the log's current number.</param>
    /// <returns>The subscriber, whose reader yields what the pumps hand it.</returns>
    public PgLogSubscriber<TEntry> Add(long cursor, bool resyncFirst)
    {
        var subscriber = new PgLogSubscriber<TEntry>(cursor, resyncFirst);

        lock (Gate)
        {
            Subscribers.Add(subscriber);
        }

        return subscriber;
    }

    /// <summary>
    /// Removes a subscriber.
    /// </summary>
    /// <param name="subscriber">The subscriber to remove.</param>
    /// <returns>True when the subscriber was the last one; otherwise, false.</returns>
    public bool Remove(PgLogSubscriber<TEntry> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        lock (Gate)
        {
            return Subscribers.Remove(subscriber) && Subscribers.Count == 0;
        }
    }

    /// <inheritdoc />
    public void ResyncAll()
    {
        foreach (var subscriber in Snapshot())
        {
            subscriber.NeedsResync = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask PumpAsync(CancellationToken cancellationToken)
    {
        await Pumping.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var subscribers = Snapshot();
                if (subscribers.Length == 0)
                {
                    return;
                }

                var lowest = Lowest(subscribers);
                var page = await Read(lowest, PageSize, cancellationToken).ConfigureAwait(false);
                foreach (var subscriber in subscribers)
                {
                    Deliver(subscriber, page);
                }

                if (page.Entries.Length < PageSize)
                {
                    return;
                }

                // Read again only while a subscriber is still behind and the last page moved the lowest cursor.
                var next = Lowest(subscribers);
                if (next <= lowest || next >= page.Current)
                {
                    return;
                }
            }
        }
        finally
        {
            Pumping.Release();
        }
    }

    /// <summary>
    /// Copies the subscribers.
    /// </summary>
    /// <returns>The subscribers.</returns>
    private PgLogSubscriber<TEntry>[] Snapshot()
    {
        lock (Gate)
        {
            return [.. Subscribers];
        }
    }
}
