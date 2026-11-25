// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A bounded queue of events for one reader, which drops what it holds when it is full.
/// </summary>
/// <remarks>
/// Any thread may post. One reader takes items in order; a full queue drops every item it holds and keeps the newest
/// item dropped, which the reader takes next, flagged as an overflow, so that it can report the gap.
/// </remarks>
/// <typeparam name="T">The type of the items the queue holds.</typeparam>
internal class InProcessEventQueue<T>
    where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessEventQueue{T}"/> class.
    /// </summary>
    /// <param name="capacity">The number of items the queue holds before it drops them.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than one.</exception>
    public InProcessEventQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
    }

    /// <summary>
    /// Gets the number of items the queue holds before it drops them.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the lock that guards the queue's state.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the items posted and not yet taken, oldest first.
    /// </summary>
    private Queue<T> Items { get; } = new();

    /// <summary>
    /// Gets or sets the newest item dropped by an overflow that the reader has not yet taken, or null for none.
    /// </summary>
    private T? Dropped { get; set; }

    /// <summary>
    /// Gets or sets the signal the waiting reader awaits, or null when no reader waits.
    /// </summary>
    private TaskCompletionSource? Waiter { get; set; }

    /// <summary>
    /// Posts an item, dropping what the queue holds when it is full.
    /// </summary>
    /// <param name="item">The item to post.</param>
    public void Post(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var waiter = Enqueue(item);

        waiter?.TrySetResult();
    }

    /// <summary>
    /// Takes the next item, waiting until one is posted.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>
    /// The item taken, or the newest item dropped, flagged as an overflow, when the queue was full since the last take.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<InProcessQueued<T>> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var signal = TryTake(out var queued);
            if (signal is null)
            {
                return queued;
            }

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds an item under the lock.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>The signal of the waiting reader to complete, or null when no reader waits.</returns>
    private TaskCompletionSource? Enqueue(T item)
    {
        using (Gate.EnterScope())
        {
            if (Items.Count == Capacity)
            {
                Items.Clear();
                Dropped = item;
            }
            else
            {
                Items.Enqueue(item);
            }

            var waiter = Waiter;
            Waiter = null;
            return waiter;
        }
    }

    /// <summary>
    /// Takes the next item under the lock, or registers the reader's signal when there is none.
    /// </summary>
    /// <param name="queued">The item taken, when the method returns null.</param>
    /// <returns>The task that completes when an item is posted, or null when an item was taken.</returns>
    private Task? TryTake(out InProcessQueued<T> queued)
    {
        using (Gate.EnterScope())
        {
            if (Dropped is { } dropped)
            {
                Dropped = null;
                queued = new InProcessQueued<T>(dropped, Overflowed: true);
                return null;
            }

            if (Items.TryDequeue(out var item))
            {
                queued = new InProcessQueued<T>(item, Overflowed: false);
                return null;
            }

            Waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queued = default;
            return Waiter.Task;
        }
    }
}
