// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The membership groups of the in-process provider, kept in the process's memory: a member ends at a leave or with
/// its session.
/// </summary>
internal class InProcessMembershipService : IMembershipService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessMembershipService"/> class.
    /// </summary>
    /// <param name="sessions">The session table, which shares its lock with the record table.</param>
    public InProcessMembershipService(InProcessSessionTable sessions)
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
    /// Gets the members of each group, in the order they joined.
    /// </summary>
    private Dictionary<CoordinationKey, List<Member>> Groups { get; } = [];

    /// <summary>
    /// Gets the queues of the watches of each group.
    /// </summary>
    private Dictionary<
        CoordinationKey,
        List<InProcessEventQueue<MembershipView>>
    > Watchers { get; } = [];

    /// <summary>
    /// Gets the groups that hold an entry of each session, so a session's end looks up where it is a member.
    /// </summary>
    private Dictionary<SessionId, HashSet<CoordinationKey>> GroupsBySession { get; } = [];

    /// <inheritdoc />
    public ValueTask<MembershipJoinStatus> JoinAsync(
        CoordinationKey group,
        Member member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(member);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        if (!Sessions.IsLive(member.Session))
        {
            return ValueTask.FromResult(MembershipJoinStatus.SessionEnded);
        }

        if (Groups.TryGetValue(group, out var members))
        {
            foreach (var other in members)
            {
                if (
                    string.Equals(other.NodeId, member.NodeId, StringComparison.Ordinal)
                    && !other.Session.Equals(member.Session)
                )
                {
                    return ValueTask.FromResult(MembershipJoinStatus.NodeIdTaken);
                }
            }
        }
        else
        {
            members = [];
            Groups[group] = members;
        }

        members.RemoveAll(other => other.Session.Equals(member.Session));
        members.Add(member);
        Bind(member.Session, group);
        Changed(group);

        return ValueTask.FromResult(MembershipJoinStatus.Joined);
    }

    /// <inheritdoc />
    public ValueTask LeaveAsync(
        CoordinationKey group,
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(session);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        if (Remove(group, session))
        {
            Changed(group);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<MembershipView> ReadAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        var view = View(group);

        return ValueTask.FromResult(view);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        InProcessGuard.Check(options, cancellationToken);

        return WatchCoreAsync(group, cancellationToken);
    }

    /// <summary>
    /// Streams the views of a group, registering the watch on the first read and removing it when the stream ends.
    /// </summary>
    /// <param name="group">The group to watch.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of views.</returns>
    private async IAsyncEnumerable<MembershipView> WatchCoreAsync(
        CoordinationKey group,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var queue = new InProcessEventQueue<MembershipView>(capacity: 1);

        Start(group, queue);

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
            Stop(group, queue);
        }
    }

    /// <summary>
    /// Adds a watch's queue to a group and posts the group's current view to it, taking the lock in a method that does
    /// not await.
    /// </summary>
    /// <param name="group">The group to watch.</param>
    /// <param name="queue">The queue of the watch.</param>
    private void Start(CoordinationKey group, InProcessEventQueue<MembershipView> queue)
    {
        using var scope = Sessions.Enter();

        if (!Watchers.TryGetValue(group, out var queues))
        {
            queues = [];
            Watchers[group] = queues;
        }

        queues.Add(queue);
        var view = View(group);

        queue.Post(view);
    }

    /// <summary>
    /// Removes a watch's queue from a group, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="group">The group the watch was of.</param>
    /// <param name="queue">The queue of the watch.</param>
    private void Stop(CoordinationKey group, InProcessEventQueue<MembershipView> queue)
    {
        using var scope = Sessions.Enter();

        if (!Watchers.TryGetValue(group, out var queues))
        {
            return;
        }

        queues.Remove(queue);

        if (queues.Count == 0)
        {
            Watchers.Remove(group);
        }
    }

    /// <summary>
    /// Gets the view of a group.
    /// </summary>
    /// <param name="group">The group to view.</param>
    /// <returns>
    /// The view of the group at its keyspace's current revision, with no members when the group has none.
    /// </returns>
    private MembershipView View(CoordinationKey group)
    {
        var revision = Sessions.Records.Current(group.Keyspace);
        var members = Groups.TryGetValue(group, out var found) ? found : [];

        return new MembershipView(group, revision, members);
    }

    /// <summary>
    /// Records that a session has an entry in a group.
    /// </summary>
    /// <param name="session">The session that joined.</param>
    /// <param name="group">The group the session joined.</param>
    private void Bind(SessionId session, CoordinationKey group)
    {
        if (!GroupsBySession.TryGetValue(session, out var groups))
        {
            groups = [];
            GroupsBySession[session] = groups;
        }

        groups.Add(group);
    }

    /// <summary>
    /// Removes the record that a session has an entry in a group, dropping the session when it is in no other group.
    /// </summary>
    /// <param name="session">The session that left.</param>
    /// <param name="group">The group the session left.</param>
    private void Unbind(SessionId session, CoordinationKey group)
    {
        if (!GroupsBySession.TryGetValue(session, out var groups))
        {
            return;
        }

        groups.Remove(group);

        if (groups.Count == 0)
        {
            GroupsBySession.Remove(session);
        }
    }

    /// <summary>
    /// Removes a session's entries from a group and drops the group when it is left empty.
    /// </summary>
    /// <param name="group">The group to remove the session from.</param>
    /// <param name="session">The session whose entries are removed.</param>
    /// <returns>True when an entry was removed; otherwise, false.</returns>
    private bool Remove(CoordinationKey group, SessionId session)
    {
        if (!Groups.TryGetValue(group, out var members))
        {
            return false;
        }

        var removed = members.RemoveAll(other => other.Session.Equals(session)) > 0;

        if (removed)
        {
            Unbind(session, group);
        }

        if (members.Count == 0)
        {
            Groups.Remove(group);
        }

        return removed;
    }

    /// <summary>
    /// Advances the revision of a group's keyspace and posts the group's view to each of its watches.
    /// </summary>
    /// <param name="group">The group whose members changed.</param>
    private void Changed(CoordinationKey group)
    {
        Sessions.Records.Advance(group.Keyspace);

        if (!Watchers.TryGetValue(group, out var queues))
        {
            return;
        }

        var view = View(group);

        foreach (var queue in queues)
        {
            queue.Post(view);
        }
    }

    /// <summary>
    /// Removes the entries of a session that ended from every group.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void EndSession(SessionId session)
    {
        if (!GroupsBySession.TryGetValue(session, out var found))
        {
            return;
        }

        CoordinationKey[] groups = [.. found];

        foreach (var group in groups)
        {
            if (Remove(group, session))
            {
                Changed(group);
            }
        }
    }
}
