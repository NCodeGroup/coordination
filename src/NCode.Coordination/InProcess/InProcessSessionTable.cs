// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The live sessions of the in-process provider, the tokens the provider issues and the handlers that run when a
/// session ends.
/// </summary>
/// <remarks>
/// The table shares the lock of the record table, so a session's end removes what is bound to it in one critical
/// section. The table is not thread-safe: every method except <see cref="Enter"/> is called inside
/// <see cref="Enter"/>. An end handler runs while the lock is held and never calls <see cref="Enter"/>.
/// </remarks>
internal class InProcessSessionTable
{
    /// <summary>
    /// The text that a session's token starts with.
    /// </summary>
    internal const string SessionTokenPrefix = "s";

    /// <summary>
    /// The text that an owner token starts with.
    /// </summary>
    internal const string OwnerTokenPrefix = "o";

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessSessionTable"/> class.
    /// </summary>
    /// <param name="records">The record table, whose lock and deadlines the sessions share.</param>
    public InProcessSessionTable(InProcessRecordTable records)
    {
        ArgumentNullException.ThrowIfNull(records);

        Records = records;
        OnEnd(records.DeleteBoundTo);
    }

    /// <summary>
    /// Gets the record table, whose lock and deadlines the sessions share.
    /// </summary>
    public InProcessRecordTable Records { get; }

    /// <summary>
    /// Gets the clock the sessions' time to live is measured on.
    /// </summary>
    public TimeProvider Time => Records.Time;

    /// <summary>
    /// Gets the deadlines of the state that the lock guards.
    /// </summary>
    public InProcessDeadlines Deadlines => Records.Deadlines;

    /// <summary>
    /// Gets the live sessions.
    /// </summary>
    private Dictionary<SessionId, InProcessSession> Sessions { get; } = [];

    /// <summary>
    /// Gets the handlers that run, in order, when a session ends.
    /// </summary>
    private List<Action<SessionId>> EndHandlers { get; } = [];

    /// <summary>
    /// Gets or sets the number of the latest token issued.
    /// </summary>
    private long LastIssued { get; set; }

    /// <summary>
    /// Gets or sets the number of the latest fencing token issued.
    /// </summary>
    private long LastFencingToken { get; set; }

    /// <summary>
    /// Takes the lock and runs the deadlines that are due.
    /// </summary>
    /// <returns>The scope that releases the lock when it is disposed.</returns>
    public Lock.Scope Enter() => Records.Enter();

    /// <summary>
    /// Adds a handler that runs, after the handlers added before it, when a session ends.
    /// </summary>
    /// <param name="handler">The handler, which receives the id of the session that ended.</param>
    public void OnEnd(Action<SessionId> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        EndHandlers.Add(handler);
    }

    /// <summary>
    /// Issues a token that no earlier call issued.
    /// </summary>
    /// <param name="prefix">The text the token starts with.</param>
    /// <returns>The prefix followed by the number of the token.</returns>
    public string Issue(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return prefix + (++LastIssued).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Issues a fencing token greater than every fencing token issued before it.
    /// </summary>
    /// <returns>The fencing token.</returns>
    public FencingToken NextFencingToken() => new(++LastFencingToken);

    /// <summary>
    /// Opens a session that lives for a time to live unless it is kept alive.
    /// </summary>
    /// <param name="keyspace">The keyspace the session lives in.</param>
    /// <param name="timeToLive">The time to live of the session.</param>
    /// <returns>The session's id and the time to live it was granted, which is the one requested.</returns>
    public SessionGrant Open(Keyspace keyspace, TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(timeToLive);

        var token = Issue(SessionTokenPrefix);
        var id = new SessionId(keyspace, token);
        var session = new InProcessSession(id, timeToLive);

        Sessions[id] = session;
        Schedule(session);

        return new SessionGrant(id, timeToLive);
    }

    /// <summary>
    /// Determines whether a session is live.
    /// </summary>
    /// <param name="id">The id of the session.</param>
    /// <returns>True when the session was opened and has not ended.</returns>
    public bool IsLive(SessionId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Sessions.ContainsKey(id);
    }

    /// <summary>
    /// Renews a session for its time to live.
    /// </summary>
    /// <param name="id">The id of the session.</param>
    /// <returns>The renewed result, or the ended result when the session is not live.</returns>
    public KeepAliveResult KeepAlive(SessionId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!Sessions.TryGetValue(id, out var session))
        {
            return KeepAliveResult.Ended;
        }

        Deadlines.Cancel(session.Deadline);
        Schedule(session);

        return KeepAliveResult.Renewed(session.TimeToLive);
    }

    /// <summary>
    /// Ends a session and everything bound to it. Closing a session that is not live does nothing.
    /// </summary>
    /// <param name="id">The id of the session.</param>
    public void Close(SessionId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        End(id);
    }

    /// <summary>
    /// Gets the task that completes when a session ends.
    /// </summary>
    /// <param name="id">The id of the session.</param>
    /// <returns>A completed task when the session is not live, and otherwise the task of the session's end.</returns>
    public Task WaitForEnd(SessionId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Sessions.TryGetValue(id, out var session) ? session.Ended.Task : Task.CompletedTask;
    }

    /// <summary>
    /// Adds the deadline that ends a session a time to live from now.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Schedule(InProcessSession session)
    {
        var expiry = InProcessExpiry.Begin(Time, session.TimeToLive);
        session.Deadline = Deadlines.Add(expiry, () => End(session.Id));
    }

    /// <summary>
    /// Removes a session, runs the end handlers and completes the session's end.
    /// </summary>
    /// <param name="id">The id of the session.</param>
    private void End(SessionId id)
    {
        if (!Sessions.Remove(id, out var session))
        {
            return;
        }

        Deadlines.Cancel(session.Deadline);

        foreach (var handler in EndHandlers)
        {
            handler(id);
        }

        session.Ended.TrySetResult();
    }
}
