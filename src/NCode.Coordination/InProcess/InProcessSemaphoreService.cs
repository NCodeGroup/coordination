// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The semaphores of the in-process provider, kept in the process's memory with each slot ending at a release, after
/// its time to live, or with its session.
/// </summary>
internal class InProcessSemaphoreService : ISemaphoreService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessSemaphoreService"/> class.
    /// </summary>
    /// <param name="sessions">The session table, which shares its lock with the record table.</param>
    public InProcessSemaphoreService(InProcessSessionTable sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        Sessions = sessions;
        sessions.OnEnd(EndSession);
    }

    /// <summary>
    /// Gets the session table, which shares its lock with the record table.
    /// </summary>
    private InProcessSessionTable Sessions { get; }

    /// <summary>
    /// Gets the semaphores by the resource they are on, holding only those with a slot held.
    /// </summary>
    private Dictionary<CoordinationKey, InProcessSemaphore> Semaphores { get; } = [];

    /// <summary>
    /// Gets the sources of the tasks that wait for a slot of a resource to be free.
    /// </summary>
    private Dictionary<CoordinationKey, TaskCompletionSource> Waiters { get; } = [];

    /// <inheritdoc />
    public ValueTask<SemaphoreAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        int capacity,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            capacity,
            CoordinationLimits.MaxSemaphoreCapacity
        );
        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            if (terms.IsBoundToSession && !Sessions.IsLive(terms.Session))
            {
                return ValueTask.FromResult(SemaphoreAcquireResult.SessionEnded);
            }

            if (Semaphores.TryGetValue(resource, out var semaphore))
            {
                if (semaphore.Capacity != capacity)
                {
                    return ValueTask.FromResult(SemaphoreAcquireResult.CapacityConflict);
                }

                if (terms.IsBoundToSession)
                {
                    var held = FindHeld(semaphore, terms.Session);

                    if (held is not null)
                    {
                        return ValueTask.FromResult(SemaphoreAcquireResult.Acquired(held.Grant));
                    }
                }
            }
            else
            {
                semaphore = new InProcessSemaphore(capacity);
            }

            var slot = Array.IndexOf(semaphore.Slots, null);

            if (slot < 0)
            {
                return ValueTask.FromResult(SemaphoreAcquireResult.Full);
            }

            var owner = new OwnerToken(Sessions.Issue("o"));
            var grant = new SemaphoreGrant(
                resource,
                slot,
                Sessions.NextFencingToken(),
                owner,
                terms
            );
            var created = new InProcessHold<SemaphoreGrant>(grant, owner, terms);

            if (!terms.IsBoundToSession)
            {
                created.Deadline = Schedule(resource, slot, owner, terms.TimeToLive);
            }

            semaphore.Slots[slot] = created;
            semaphore.Held++;
            Semaphores[resource] = semaphore;

            return ValueTask.FromResult(SemaphoreAcquireResult.Acquired(grant));
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        SemaphoreGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(timeToLive);

        if (grant.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A grant bound to a session lives as its session does and is not renewed.",
                nameof(grant)
            );
        }

        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            var hold = FindHold(grant);

            if (hold is null)
            {
                return ValueTask.FromResult(false);
            }

            Sessions.Deadlines.Cancel(hold.Deadline);
            hold.Deadline = Schedule(grant.Resource, grant.Slot, grant.Owner, timeToLive);

            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ReleaseAsync(
        SemaphoreGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            if (FindHold(grant) is null)
            {
                return ValueTask.FromResult(false);
            }

            Free(grant.Resource, grant.Slot);

            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public async ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        InProcessGuard.Check(options, cancellationToken);

        await StartWait(resource).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the task that completes when a slot of a resource is free, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="resource">The resource to wait for.</param>
    /// <returns>A completed task when a slot is free, and otherwise the task of the next release.</returns>
    private Task StartWait(CoordinationKey resource)
    {
        using (Sessions.Enter())
        {
            if (
                !Semaphores.TryGetValue(resource, out var semaphore)
                || semaphore.Held < semaphore.Capacity
            )
            {
                return Task.CompletedTask;
            }

            if (!Waiters.TryGetValue(resource, out var waiter))
            {
                waiter = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                Waiters[resource] = waiter;
            }

            return waiter.Task;
        }
    }

    /// <summary>
    /// Finds the slot a session holds in a semaphore.
    /// </summary>
    /// <param name="semaphore">The semaphore to look in.</param>
    /// <param name="session">The session that may hold a slot.</param>
    /// <returns>The hold of the session, or null when it holds no slot.</returns>
    private static InProcessHold<SemaphoreGrant>? FindHeld(
        InProcessSemaphore semaphore,
        SessionId session
    )
    {
        foreach (var hold in semaphore.Slots)
        {
            if (hold is not null && session.Equals(hold.Terms.Session))
            {
                return hold;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the hold a grant stands for.
    /// </summary>
    /// <param name="grant">The grant to look for.</param>
    /// <returns>The hold of the grant's slot when its owner token matches, and otherwise null.</returns>
    private InProcessHold<SemaphoreGrant>? FindHold(SemaphoreGrant grant)
    {
        if (
            !Semaphores.TryGetValue(grant.Resource, out var semaphore)
            || grant.Slot >= semaphore.Capacity
        )
        {
            return null;
        }

        var hold = semaphore.Slots[grant.Slot];

        return hold is not null && hold.Owner == grant.Owner ? hold : null;
    }

    /// <summary>
    /// Adds the deadline that ends a grant made for a time to live.
    /// </summary>
    /// <param name="resource">The resource the semaphore is on.</param>
    /// <param name="slot">The index of the slot the grant holds.</param>
    /// <param name="owner">The owner token of the grant.</param>
    /// <param name="timeToLive">How long the grant lasts from now.</param>
    /// <returns>The handle that cancels the deadline.</returns>
    private InProcessDeadline Schedule(
        CoordinationKey resource,
        int slot,
        OwnerToken owner,
        TimeToLive timeToLive
    ) =>
        Sessions.Deadlines.Add(InProcessExpiry.Begin(Sessions.Time, timeToLive), () =>
            Expire(resource, slot, owner)
        );

    /// <summary>
    /// Frees a slot, removes the semaphore when no slot is held and wakes the tasks that wait for a free slot.
    /// </summary>
    /// <param name="resource">The resource the semaphore is on.</param>
    /// <param name="slot">The index of the slot that ends.</param>
    private void Free(CoordinationKey resource, int slot)
    {
        if (Semaphores.TryGetValue(resource, out var semaphore))
        {
            var hold = semaphore.Slots[slot];

            if (hold is not null)
            {
                Sessions.Deadlines.Cancel(hold.Deadline);
                semaphore.Slots[slot] = null;
                semaphore.Held--;
            }

            if (semaphore.Held == 0)
            {
                Semaphores.Remove(resource);
            }
        }

        if (Waiters.Remove(resource, out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    /// <summary>
    /// Ends a slot whose time to live has passed, unless the slot has since been granted to another owner.
    /// </summary>
    /// <param name="resource">The resource whose deadline came due.</param>
    /// <param name="slot">The index of the slot the deadline belongs to.</param>
    /// <param name="owner">The owner token of the grant the deadline belongs to.</param>
    private void Expire(CoordinationKey resource, int slot, OwnerToken owner)
    {
        if (
            Semaphores.TryGetValue(resource, out var semaphore)
            && semaphore.Slots[slot] is { } hold
            && hold.Owner == owner
        )
        {
            Free(resource, slot);
        }
    }

    /// <summary>
    /// Ends every slot bound to a session that ended.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void EndSession(SessionId session)
    {
        var ended = new List<(CoordinationKey Resource, int Slot)>();

        foreach (var (resource, semaphore) in Semaphores)
        {
            for (var slot = 0; slot < semaphore.Slots.Length; slot++)
            {
                if (semaphore.Slots[slot] is { } hold && session.Equals(hold.Terms.Session))
                {
                    ended.Add((resource, slot));
                }
            }
        }

        foreach (var (resource, slot) in ended)
        {
            Free(resource, slot);
        }
    }
}
