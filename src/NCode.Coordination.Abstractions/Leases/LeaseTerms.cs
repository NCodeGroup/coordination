// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// How long a grant of a lease, a semaphore slot or a leadership lasts: as long as a session, or for a time to live
/// the holder renews.
/// </summary>
/// <remarks>
/// Create terms with <see cref="ForSession(SessionId)"/> or <see cref="ForTimeToLive(TimeToLive)"/>. Terms bound to a
/// session have no time to live, and terms for a time to live have no session.
/// </remarks>
public sealed class LeaseTerms
{
    private LeaseTerms(SessionId? session, TimeToLive? timeToLive)
    {
        Session = session;
        TimeToLive = timeToLive;
    }

    /// <summary>
    /// Gets the session the grant lives as long as, or null when the grant lasts for a time to live.
    /// </summary>
    public SessionId? Session { get; }

    /// <summary>
    /// Gets the time to live the holder renews, or null when the grant is bound to a session.
    /// </summary>
    public TimeToLive? TimeToLive { get; }

    /// <summary>
    /// Gets a value indicating whether the grant is bound to a session.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Session))]
    [MemberNotNullWhen(false, nameof(TimeToLive))]
    public bool IsBoundToSession => Session is not null;

    /// <summary>
    /// Creates terms for a grant that lasts as long as a session.
    /// </summary>
    /// <param name="session">The session the grant is bound to.</param>
    /// <returns>The terms bound to the session.</returns>
    /// <exception cref="ArgumentNullException">The session is null.</exception>
    public static LeaseTerms ForSession(SessionId session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new LeaseTerms(session, timeToLive: null);
    }

    /// <summary>
    /// Creates terms for a grant that lasts for a time to live the holder renews.
    /// </summary>
    /// <param name="timeToLive">How long the grant lasts before it is renewed.</param>
    /// <returns>The terms for the time to live.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static LeaseTerms ForTimeToLive(TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(timeToLive);

        return new LeaseTerms(session: null, timeToLive);
    }
}
