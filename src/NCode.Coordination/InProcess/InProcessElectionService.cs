// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The elections of the in-process provider, kept in the process's memory: a leadership ends at a resignation, after
/// its time to live, or with its session.
/// </summary>
internal class InProcessElectionService : IElectionService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessElectionService"/> class.
    /// </summary>
    /// <param name="sessions">The session table, which shares its lock with the record table.</param>
    public InProcessElectionService(InProcessSessionTable sessions)
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
    /// Gets the leaderships by the role they lead.
    /// </summary>
    private Dictionary<CoordinationKey, InProcessHold<Leadership>> Holds { get; } = [];

    /// <summary>
    /// Gets the queues of the watches of each role.
    /// </summary>
    private Dictionary<CoordinationKey, List<InProcessEventQueue<ElectionView>>> Watchers { get; } =
    [];

    /// <summary>
    /// Gets the roles whose leadership is bound to each session, so a session's end looks up what it leads.
    /// </summary>
    private Dictionary<SessionId, HashSet<CoordinationKey>> RolesBySession { get; } = [];

    /// <inheritdoc />
    public ValueTask<ElectionResult> TryLeadAsync(
        CoordinationKey role,
        ReadOnlyMemory<byte> value,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(terms);
        InProcessGuard.CheckValue(value, nameof(value));
        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            if (terms.IsBoundToSession && !Sessions.IsLive(terms.Session))
            {
                return ValueTask.FromResult(ElectionResult.SessionEnded);
            }

            if (Holds.TryGetValue(role, out var hold))
            {
                var isSameSession =
                    terms.IsBoundToSession && terms.Session.Equals(hold.Terms.Session);

                var existing = isSameSession
                    ? ElectionResult.Elected(hold.Grant)
                    : ElectionResult.Led;

                return ValueTask.FromResult(existing);
            }

            var ownerValue = Sessions.Issue("o");
            var owner = new OwnerToken(ownerValue);
            var fencingToken = Sessions.NextFencingToken();
            var leader = new Leader(role, fencingToken, value);
            var leadership = new Leadership(leader, owner, terms);
            var created = new InProcessHold<Leadership>(leadership, owner, terms);

            if (!terms.IsBoundToSession)
            {
                created.Deadline = Schedule(role, owner, terms.TimeToLive);
            }
            else
            {
                Bind(terms.Session, role);
            }

            Holds[role] = created;
            Notify(role);

            var elected = ElectionResult.Elected(leadership);

            return ValueTask.FromResult(elected);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        Leadership leadership,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        ArgumentNullException.ThrowIfNull(timeToLive);

        if (leadership.Terms.IsBoundToSession)
        {
            throw new ArgumentException(
                "A leadership bound to a session lives as its session does and is not renewed.",
                nameof(leadership)
            );
        }

        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            var role = leadership.Leader.Role;

            if (!Holds.TryGetValue(role, out var hold) || hold.Owner != leadership.Owner)
            {
                return ValueTask.FromResult(false);
            }

            Sessions.Deadlines.Cancel(hold.Deadline);
            hold.Deadline = Schedule(role, leadership.Owner, timeToLive);

            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);
        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            var role = leadership.Leader.Role;

            if (!Holds.TryGetValue(role, out var hold) || hold.Owner != leadership.Owner)
            {
                return ValueTask.FromResult(false);
            }

            Vacate(role);

            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        InProcessGuard.Check(options, cancellationToken);

        using (Sessions.Enter())
        {
            var view = View(role);

            return ValueTask.FromResult(view);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ElectionView> WatchAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        InProcessGuard.Check(options, cancellationToken);

        return WatchCoreAsync(role, cancellationToken);
    }

    /// <summary>
    /// Streams the views of a role, registering the watch on the first read and removing it when the stream ends.
    /// </summary>
    /// <param name="role">The role to watch.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of views.</returns>
    private async IAsyncEnumerable<ElectionView> WatchCoreAsync(
        CoordinationKey role,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var queue = new InProcessEventQueue<ElectionView>(capacity: 1);

        Start(role, queue);

        try
        {
            while (true)
            {
                var next = await queue.TakeAsync(cancellationToken).ConfigureAwait(false);

                yield return next.Item;
            }
        }
        finally
        {
            Stop(role, queue);
        }
    }

    /// <summary>
    /// Adds a watch's queue to a role and posts the role's current view to it, taking the lock in a method that does
    /// not await.
    /// </summary>
    /// <param name="role">The role to watch.</param>
    /// <param name="queue">The queue of the watch.</param>
    private void Start(CoordinationKey role, InProcessEventQueue<ElectionView> queue)
    {
        using (Sessions.Enter())
        {
            if (!Watchers.TryGetValue(role, out var queues))
            {
                queues = [];
                Watchers[role] = queues;
            }

            queues.Add(queue);
            var view = View(role);

            queue.Post(view);
        }
    }

    /// <summary>
    /// Removes a watch's queue from a role, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="role">The role the watch was of.</param>
    /// <param name="queue">The queue of the watch.</param>
    private void Stop(CoordinationKey role, InProcessEventQueue<ElectionView> queue)
    {
        using (Sessions.Enter())
        {
            if (!Watchers.TryGetValue(role, out var queues))
            {
                return;
            }

            queues.Remove(queue);

            if (queues.Count == 0)
            {
                Watchers.Remove(role);
            }
        }
    }

    /// <summary>
    /// Gets the view of a role.
    /// </summary>
    /// <param name="role">The role to view.</param>
    /// <returns>The view of the role, whose leader is null when it has none.</returns>
    private ElectionView View(CoordinationKey role)
    {
        var leader = Holds.TryGetValue(role, out var hold) ? hold.Grant.Leader : null;

        return new ElectionView(role, leader);
    }

    /// <summary>
    /// Posts the current view of a role to each of its watches.
    /// </summary>
    /// <param name="role">The role whose leader changed.</param>
    private void Notify(CoordinationKey role)
    {
        if (!Watchers.TryGetValue(role, out var queues))
        {
            return;
        }

        var view = View(role);

        foreach (var queue in queues)
        {
            queue.Post(view);
        }
    }

    /// <summary>
    /// Adds the deadline that ends a leadership made for a time to live.
    /// </summary>
    /// <param name="role">The role the leadership leads.</param>
    /// <param name="owner">The owner token of the leadership.</param>
    /// <param name="timeToLive">How long the leadership lasts from now.</param>
    /// <returns>The handle that cancels the deadline.</returns>
    private InProcessDeadline Schedule(
        CoordinationKey role,
        OwnerToken owner,
        TimeToLive timeToLive
    )
    {
        var expiry = InProcessExpiry.Begin(Sessions.Time, timeToLive);

        return Sessions.Deadlines.Add(expiry, () => Expire(role, owner));
    }

    /// <summary>
    /// Records that a role's leadership is bound to a session.
    /// </summary>
    /// <param name="session">The session the leadership is bound to.</param>
    /// <param name="role">The role the leadership leads.</param>
    private void Bind(SessionId session, CoordinationKey role)
    {
        if (!RolesBySession.TryGetValue(session, out var roles))
        {
            roles = [];
            RolesBySession[session] = roles;
        }

        roles.Add(role);
    }

    /// <summary>
    /// Removes the binding of a role's leadership to a session, dropping the session when it leads nothing else.
    /// </summary>
    /// <param name="session">The session the leadership was bound to.</param>
    /// <param name="role">The role the leadership led.</param>
    private void Unbind(SessionId session, CoordinationKey role)
    {
        if (!RolesBySession.TryGetValue(session, out var roles))
        {
            return;
        }

        roles.Remove(role);

        if (roles.Count == 0)
        {
            RolesBySession.Remove(session);
        }
    }

    /// <summary>
    /// Removes a role's leadership and tells the role's watches.
    /// </summary>
    /// <param name="role">The role whose leadership ends.</param>
    private void Vacate(CoordinationKey role)
    {
        if (Holds.Remove(role, out var hold))
        {
            Sessions.Deadlines.Cancel(hold.Deadline);

            if (hold.Terms.IsBoundToSession)
            {
                Unbind(hold.Terms.Session, role);
            }
        }

        Notify(role);
    }

    /// <summary>
    /// Ends a leadership whose time to live has passed, unless the role has since been led by another owner.
    /// </summary>
    /// <param name="role">The role whose deadline came due.</param>
    /// <param name="owner">The owner token of the leadership the deadline belongs to.</param>
    private void Expire(CoordinationKey role, OwnerToken owner)
    {
        if (Holds.TryGetValue(role, out var hold) && hold.Owner == owner)
        {
            Vacate(role);
        }
    }

    /// <summary>
    /// Ends every leadership bound to a session that ended.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void EndSession(SessionId session)
    {
        if (!RolesBySession.TryGetValue(session, out var roles))
        {
            return;
        }

        CoordinationKey[] ended = [.. roles];

        foreach (var role in ended)
        {
            Vacate(role);
        }
    }
}
