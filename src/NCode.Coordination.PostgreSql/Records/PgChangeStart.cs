// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds what the first batch of a keyspace's change read: the change that the transaction builds, and the rows
/// that the asked keys hold.
/// </summary>
/// <param name="Change">The change, built from the keyspace's revision and the server's instant.</param>
/// <param name="Rows">The rows of the asked keys that exist, expired or not, by key.</param>
/// <param name="Sessions">The deadlines of the asked sessions whose rows exist, by session id.</param>
internal sealed record PgChangeStart(
    PgKeyspaceChange Change,
    ImmutableDictionary<CoordinationKey, PgRecordRow> Rows,
    ImmutableDictionary<string, DateTime> Sessions
)
{
    /// <summary>
    /// Ends every session that the read met past its deadline, whether a row is bound to it or it was asked for.
    /// </summary>
    public void EndExpiredSessions()
    {
        var expired = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var row in Rows.Values)
        {
            if (
                row.Session is { } session
                && row.SessionDeadline is { } deadline
                && deadline <= Change.Now
            )
            {
                expired.Add(session);
            }
        }

        foreach (var (session, deadline) in Sessions)
        {
            if (deadline <= Change.Now)
            {
                expired.Add(session);
            }
        }

        foreach (var session in expired)
        {
            Change.EndSession(session);
        }
    }

    /// <summary>
    /// Tells whether a session was asked for, exists and has not passed its deadline.
    /// </summary>
    /// <param name="session">The session's id.</param>
    /// <returns>True when the session is live.</returns>
    public bool IsLiveSession(string session) =>
        Sessions.TryGetValue(session, out var deadline) && deadline > Change.Now;

    /// <summary>
    /// Tells whether a lifetime binds a record to a session that has ended or was never opened in the keyspace.
    /// </summary>
    /// <param name="lifetime">The lifetime.</param>
    /// <param name="session">The id of the lifetime's session in the keyspace, or null when it has none there.</param>
    /// <returns>True when the lifetime binds to a session that is not live.</returns>
    public bool BindsToEndedSession(RecordLifetime lifetime, string? session) =>
        lifetime.Session is not null && (session is null || !IsLiveSession(session));
}
