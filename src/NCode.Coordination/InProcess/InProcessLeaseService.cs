// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The leases of the in-process provider, kept in the process's memory and ending at a release, after their time to
/// live, or with their session.
/// </summary>
internal class InProcessLeaseService : ILeaseService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessLeaseService"/> class.
    /// </summary>
    /// <param name="sessions">The session table, which shares its lock with the record table.</param>
    public InProcessLeaseService(InProcessSessionTable sessions)
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
    /// Gets the holds by the resource they lease.
    /// </summary>
    private Dictionary<CoordinationKey, InProcessHold<LeaseGrant>> Holds { get; } = [];

    /// <summary>
    /// Gets the sources of the tasks that wait for a resource to have no holder.
    /// </summary>
    private Dictionary<CoordinationKey, TaskCompletionSource> Waiters { get; } = [];

    /// <summary>
    /// Gets the resources whose hold is bound to each session, so a session's end looks up what it holds.
    /// </summary>
    private InProcessSessionIndex<CoordinationKey> ResourcesBySession { get; } = new();

    /// <inheritdoc />
    public ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        if (terms.IsBoundToSession && !Sessions.IsLive(terms.Session))
        {
            return ValueTask.FromResult(LeaseAcquireResult.SessionEnded);
        }

        if (Holds.TryGetValue(resource, out var hold))
        {
            var isSameSession = terms.IsBoundToSession && terms.Session.Equals(hold.Terms.Session);

            var existing = isSameSession
                ? LeaseAcquireResult.Acquired(hold.Grant)
                : LeaseAcquireResult.Held;

            return ValueTask.FromResult(existing);
        }

        var ownerValue = Sessions.Issue(InProcessSessionTable.OwnerTokenPrefix);
        var owner = new OwnerToken(ownerValue);
        var fencingToken = Sessions.NextFencingToken();
        var grant = new LeaseGrant(resource, fencingToken, owner, terms);
        var created = new InProcessHold<LeaseGrant>(grant, owner, terms);

        if (!terms.IsBoundToSession)
        {
            created.Deadline = Schedule(resource, owner, terms.TimeToLive);
        }
        else
        {
            ResourcesBySession.Bind(terms.Session, resource);
        }

        Holds[resource] = created;

        var acquired = LeaseAcquireResult.Acquired(grant);

        return ValueTask.FromResult(acquired);
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        LeaseGrant grant,
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

        using var scope = Sessions.Enter();

        if (!Holds.TryGetValue(grant.Resource, out var hold) || hold.Owner != grant.Owner)
        {
            return ValueTask.FromResult(false);
        }

        Sessions.Deadlines.Cancel(hold.Deadline);
        hold.Deadline = Schedule(grant.Resource, grant.Owner, timeToLive);

        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        if (!Holds.TryGetValue(grant.Resource, out var hold) || hold.Owner != grant.Owner)
        {
            return ValueTask.FromResult(false);
        }

        Free(grant.Resource);

        return ValueTask.FromResult(true);
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
    /// Gets the task that completes when a resource has no holder, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="resource">The resource to wait for.</param>
    /// <returns>A completed task when the resource has no holder, and otherwise the task of its release.</returns>
    private Task StartWait(CoordinationKey resource)
    {
        using var scope = Sessions.Enter();

        if (!Holds.ContainsKey(resource))
        {
            return Task.CompletedTask;
        }

        if (!Waiters.TryGetValue(resource, out var waiter))
        {
            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Waiters[resource] = waiter;
        }

        return waiter.Task;
    }

    /// <summary>
    /// Adds the deadline that ends a grant made for a time to live.
    /// </summary>
    /// <param name="resource">The resource the grant leases.</param>
    /// <param name="owner">The owner token of the grant.</param>
    /// <param name="timeToLive">How long the grant lasts from now.</param>
    /// <returns>The handle that cancels the deadline.</returns>
    private InProcessDeadline Schedule(
        CoordinationKey resource,
        OwnerToken owner,
        TimeToLive timeToLive
    )
    {
        var expiry = InProcessExpiry.Begin(Sessions.Time, timeToLive);

        return Sessions.Deadlines.Add(expiry, () => Expire(resource, owner));
    }

    /// <summary>
    /// Removes a resource's hold and wakes the tasks that wait for its release.
    /// </summary>
    /// <param name="resource">The resource whose hold ends.</param>
    private void Free(CoordinationKey resource)
    {
        if (Holds.Remove(resource, out var hold))
        {
            Sessions.Deadlines.Cancel(hold.Deadline);

            if (hold.Terms.IsBoundToSession)
            {
                ResourcesBySession.Unbind(hold.Terms.Session, resource);
            }
        }

        if (Waiters.Remove(resource, out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    /// <summary>
    /// Ends a hold whose time to live has passed, unless the resource has since been granted to another owner.
    /// </summary>
    /// <param name="resource">The resource whose deadline came due.</param>
    /// <param name="owner">The owner token of the grant the deadline belongs to.</param>
    private void Expire(CoordinationKey resource, OwnerToken owner)
    {
        if (Holds.TryGetValue(resource, out var hold) && hold.Owner == owner)
        {
            Free(resource);
        }
    }

    /// <summary>
    /// Ends every hold bound to a session that ended.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void EndSession(SessionId session)
    {
        var ended = ResourcesBySession.Take(session);

        foreach (var resource in ended)
        {
            Free(resource);
        }
    }
}
