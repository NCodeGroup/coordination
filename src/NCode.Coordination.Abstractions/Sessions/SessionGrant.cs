// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A session a provider opened: its identity and the time to live the provider granted.
/// </summary>
public sealed class SessionGrant
{
    /// <summary>
    /// Creates a session grant.
    /// </summary>
    /// <param name="id">The identity of the session.</param>
    /// <param name="timeToLive">The time to live the provider granted.</param>
    /// <exception cref="ArgumentNullException">The id or the time to live is null.</exception>
    public SessionGrant(SessionId id, TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(timeToLive);

        Id = id;
        TimeToLive = timeToLive;
    }

    /// <summary>
    /// Gets the identity of the session.
    /// </summary>
    public SessionId Id { get; }

    /// <summary>
    /// Gets the time to live the provider granted, never shorter than the one requested.
    /// </summary>
    public TimeToLive TimeToLive { get; }
}
