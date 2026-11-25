// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// How long a record lasts: until it is deleted, for a time to live from the write, or as long as a session.
/// </summary>
/// <remarks>
/// Use <see cref="Persistent"/>, <see cref="ForTimeToLive(TimeToLive)"/> or <see cref="ForSession(SessionId)"/>. A
/// persistent record has neither a time to live nor a session.
/// </remarks>
public sealed class RecordLifetime
{
    /// <summary>
    /// Gets the lifetime of a record that lasts until it is deleted.
    /// </summary>
    public static RecordLifetime Persistent { get; } = new(null, null);

    private RecordLifetime(TimeToLive? timeToLive, SessionId? session)
    {
        TimeToLive = timeToLive;
        Session = session;
    }

    /// <summary>
    /// Gets the time to live counted from the write, or null when the record has another lifetime.
    /// </summary>
    public TimeToLive? TimeToLive { get; }

    /// <summary>
    /// Gets the session the record lives as long as, or null when the record has another lifetime.
    /// </summary>
    public SessionId? Session { get; }

    /// <summary>
    /// Gets a value indicating whether the record lasts until it is deleted.
    /// </summary>
    public bool IsPersistent => TimeToLive is null && Session is null;

    /// <summary>
    /// Creates a lifetime that ends a time to live after the write.
    /// </summary>
    /// <param name="timeToLive">How long the record lasts from the write.</param>
    /// <returns>The lifetime for the time to live.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static RecordLifetime ForTimeToLive(TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(timeToLive);

        return new RecordLifetime(timeToLive, session: null);
    }

    /// <summary>
    /// Creates a lifetime that lasts as long as a session.
    /// </summary>
    /// <param name="session">The session the record is bound to.</param>
    /// <returns>The lifetime bound to the session.</returns>
    /// <exception cref="ArgumentNullException">The session is null.</exception>
    public static RecordLifetime ForSession(SessionId session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new RecordLifetime(timeToLive: null, session);
    }
}
