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

        using (Sessions.Enter())
        {
            return ValueTask.FromResult(Sessions.Open(keyspace, timeToLive));
        }
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

        using (Sessions.Enter())
        {
            return ValueTask.FromResult(Sessions.KeepAlive(session));
        }
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

        using (Sessions.Enter())
        {
            Sessions.Close(session);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        InProcessGuard.Check(options, cancellationToken);

        var ended = StartWait(session);

        await ended.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the task that completes when a session ends, taking the lock in a method that does not await.
    /// </summary>
    /// <param name="session">The session to wait for.</param>
    /// <returns>A completed task when the session is not live, and otherwise the task of the session's end.</returns>
    private Task StartWait(SessionId session)
    {
        using (Sessions.Enter())
        {
            return Sessions.WaitForEnd(session);
        }
    }
}
