// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The deadlines of state guarded by one lock, run in order by one timer.
/// </summary>
/// <remarks>
/// The class is not thread-safe except for <see cref="Dispose"/>: callers hold the lock, <see cref="Gate"/>, around
/// every call, and the timer takes it before it runs the deadlines that are due. The timer is never armed for longer
/// than <see cref="LongestWait"/>; when it fires early, it arms itself again for the next deadline.
/// </remarks>
internal class InProcessDeadlines : IDisposable
{
    /// <summary>
    /// The number of cancelled handles the queue may hold beyond twice the number of pending deadlines.
    /// </summary>
    internal const int CompactFloor = 1_024;

    /// <summary>
    /// Gets the longest time the timer is armed for.
    /// </summary>
    internal static TimeSpan LongestWait { get; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessDeadlines"/> class.
    /// </summary>
    /// <param name="time">The clock the deadlines are measured on.</param>
    /// <param name="gate">The lock that guards the state the deadlines belong to.</param>
    public InProcessDeadlines(TimeProvider time, Lock gate)
    {
        ArgumentNullException.ThrowIfNull(time);
        // ThrowIfNull would convert the lock to object, which the compiler reports as monitor-based locking.
#pragma warning disable CA1510 // ThrowIfNull cannot take a Lock
        if (gate is null)
        {
            throw new ArgumentNullException(nameof(gate));
        }
#pragma warning restore CA1510

        Time = time;
        Gate = gate;
        Origin = time.GetTimestamp();
        Timer = time.CreateTimer(
            _ => OnTimer(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    /// <summary>
    /// Gets the clock the deadlines are measured on.
    /// </summary>
    private TimeProvider Time { get; }

    /// <summary>
    /// Gets the lock that guards the state the deadlines belong to.
    /// </summary>
    private Lock Gate { get; }

    /// <summary>
    /// Gets the timestamp of the clock that the queue's priorities are measured from.
    /// </summary>
    private long Origin { get; }

    /// <summary>
    /// Gets the timer that runs the deadlines that are due.
    /// </summary>
    private ITimer Timer { get; }

    /// <summary>
    /// Gets the handles of the deadlines by the time they come due, measured from <see cref="Origin"/>, including
    /// handles that are done but not yet dropped.
    /// </summary>
    private PriorityQueue<InProcessDeadline, TimeSpan> Due { get; } = new();

    /// <summary>
    /// Gets or sets the time the timer was last armed for, measured from <see cref="Origin"/>, or null when it is not
    /// armed.
    /// </summary>
    private TimeSpan? Armed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the deadlines were disposed.
    /// </summary>
    private bool IsDisposed { get; set; }

    /// <summary>
    /// Gets the number of deadlines that have neither fired nor been cancelled.
    /// </summary>
    public int Pending { get; private set; }

    /// <summary>
    /// Gets the number of handles in the queue, including handles that are done but not yet dropped.
    /// </summary>
    public int Count => Due.Count;

    /// <summary>
    /// Adds a deadline.
    /// </summary>
    /// <param name="expiry">The end of the life of the state the deadline belongs to.</param>
    /// <param name="expire">The action that runs, with the lock held, when the deadline comes due.</param>
    /// <returns>The handle that cancels the deadline.</returns>
    public InProcessDeadline Add(InProcessExpiry expiry, Action expire)
    {
        ArgumentNullException.ThrowIfNull(expire);

        var at = Time.GetElapsedTime(Origin, expiry.Start) + expiry.Duration;
        var deadline = new InProcessDeadline(expire);

        Due.Enqueue(deadline, at);
        Pending++;

        if (Armed is null || at < Armed)
        {
            Arm(at);
        }

        return deadline;
    }

    /// <summary>
    /// Cancels a deadline so that it never runs.
    /// </summary>
    /// <param name="deadline">The handle of the deadline, or null for none.</param>
    public void Cancel(InProcessDeadline? deadline)
    {
        if (deadline is null || deadline.IsDone)
        {
            return;
        }

        deadline.IsDone = true;
        Pending--;

        if (Due.Count > 2 * Pending + CompactFloor)
        {
            Compact();
        }
    }

    /// <summary>
    /// Runs every deadline that is due, in the order of their times.
    /// </summary>
    public void RunDue()
    {
        var now = Time.GetElapsedTime(Origin);

        while (Due.TryPeek(out _, out var at) && at <= now)
        {
            var deadline = Due.Dequeue();
            if (deadline.IsDone)
            {
                continue;
            }

            deadline.IsDone = true;
            Pending--;
            deadline.Expire();
        }

        if (Due.TryPeek(out _, out var next))
        {
            Arm(next);
        }
        else
        {
            Armed = null;

            if (!IsDisposed)
            {
                Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Stops the timer so that no deadline runs after it.
    /// </summary>
    public void Dispose()
    {
        using (Gate.EnterScope())
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
        }

        Timer.Dispose();
    }

    /// <summary>
    /// Arms the timer for a deadline, waiting no longer than <see cref="LongestWait"/>.
    /// </summary>
    /// <param name="at">The time the deadline comes due, measured from <see cref="Origin"/>.</param>
    private void Arm(TimeSpan at)
    {
        if (IsDisposed)
        {
            return;
        }

        var wait = at - Time.GetElapsedTime(Origin);
        if (wait < TimeSpan.FromTicks(1))
        {
            wait = TimeSpan.FromTicks(1);
        }
        else if (wait > LongestWait)
        {
            wait = LongestWait;
        }

        Armed = at;
        Timer.Change(wait, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Rebuilds the queue without the handles that are done.
    /// </summary>
    private void Compact()
    {
        var live = Due.UnorderedItems.Where(item => !item.Element.IsDone).ToArray();

        Due.Clear();
        Due.EnqueueRange(live);
    }

    /// <summary>
    /// Runs the deadlines that are due when the timer fires.
    /// </summary>
    private void OnTimer()
    {
        using (Gate.EnterScope())
        {
            if (!IsDisposed)
            {
                RunDue();
            }
        }
    }
}
