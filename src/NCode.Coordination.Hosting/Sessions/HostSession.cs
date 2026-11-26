// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A session the host holds: its identity, the host's membership entry and a token that ends with the session.
/// </summary>
public sealed class HostSession
{
    /// <summary>
    /// Creates a host session.
    /// </summary>
    /// <param name="id">The identity of the session.</param>
    /// <param name="member">The host's membership entry, bound to the session.</param>
    /// <param name="ended">The token that is cancelled when the session ends.</param>
    /// <exception cref="ArgumentNullException">The id or the member is null.</exception>
    /// <exception cref="ArgumentException">The member belongs to another session.</exception>
    public HostSession(SessionId id, Member member, CancellationToken ended)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(member);

        if (member.Session != id)
        {
            throw new ArgumentException("The member belongs to another session.", nameof(member));
        }

        Id = id;
        Member = member;
        Ended = ended;
    }

    /// <summary>
    /// Gets the identity of the session.
    /// </summary>
    public SessionId Id { get; }

    /// <summary>
    /// Gets the host's membership entry, bound to the session.
    /// </summary>
    public Member Member { get; }

    /// <summary>
    /// Gets the token that is cancelled when the session ends.
    /// </summary>
    /// <remarks>
    /// The token is cancelled when the holder's deadline passes without a renewal, when a renewal finds the session
    /// ended, and as the host stops. A callback registered on it must not throw.
    /// </remarks>
    public CancellationToken Ended { get; }
}
