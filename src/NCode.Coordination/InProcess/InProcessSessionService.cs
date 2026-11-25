// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The sessions of the in-process provider, kept in the process's memory, ending at a close or after their time to
/// live, and ending what is bound to them.
/// </summary>
/// <param name="sessions">The session table, which shares its lock with the record table.</param>
internal class InProcessSessionService(InProcessSessionTable sessions) : ISessionService
{
    /// <summary>
    /// Gets the session table, which shares its lock with the record table.
    /// </summary>
    private InProcessSessionTable Sessions { get; } = sessions;

    /// <inheritdoc />
    public ValueTask<SessionGrant> OpenAsync(
        Keyspace keyspace,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(timeToLive);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        var opened = Sessions.Open(keyspace, timeToLive);

        return ValueTask.FromResult(opened);
    }

    /// <inheritdoc />
    public ValueTask<KeepAliveResult> KeepAliveAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        var result = Sessions.KeepAlive(session);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask CloseAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Sessions.Enter();

        Sessions.Close(session);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        InProcessGuard.Check(options, cancellationToken);

        var ended = StartWait(session);

        if (ended.IsCompleted)
        {
            return ValueTask.CompletedTask;
        }

        var waiting = ended.WaitAsync(cancellationToken);

        return new ValueTask(waiting);
    }

    /// <summary>
    /// Gets the task that completes when a session ends, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="session">The session to wait for.</param>
    /// <returns>A completed task when the session is not live, and otherwise the task of the session's end.</returns>
    private Task StartWait(SessionId session)
    {
        using var scope = Sessions.Enter();

        return Sessions.WaitForEnd(session);
    }
}
