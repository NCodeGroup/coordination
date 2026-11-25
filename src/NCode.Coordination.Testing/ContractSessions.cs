// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing;

/// <summary>
/// The session helpers that more than one contract suite uses.
/// </summary>
internal static class ContractSessions
{
    /// <summary>
    /// Opens a session and returns its identity.
    /// </summary>
    /// <param name="sessions">The session service to open the session with.</param>
    /// <param name="keyspace">The keyspace to open the session in.</param>
    /// <param name="timeToLive">The time to live to ask for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The identity of the session.</returns>
    public static async ValueTask<SessionId> OpenAsync(
        ISessionService sessions,
        Keyspace keyspace,
        TimeToLive timeToLive,
        CancellationToken cancellationToken
    )
    {
        var grant = await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);

        return grant.Id;
    }

    /// <summary>
    /// Opens a session, closes it and returns the identity of the session that has ended.
    /// </summary>
    /// <param name="sessions">The session service to open and close the session with.</param>
    /// <param name="keyspace">The keyspace to open the session in.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The identity of the session that has ended.</returns>
    public static async ValueTask<SessionId> OpenEndedAsync(
        ISessionService sessions,
        Keyspace keyspace,
        CancellationToken cancellationToken
    )
    {
        var session = await OpenAsync(
            sessions,
            keyspace,
            new TimeToLive(TimeSpan.FromSeconds(30)),
            cancellationToken
        );

        await sessions.CloseAsync(session, null, cancellationToken);
        return session;
    }
}
