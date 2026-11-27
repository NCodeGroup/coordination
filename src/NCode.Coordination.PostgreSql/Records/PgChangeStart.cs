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
    /// <returns>The ids of the sessions that were ended.</returns>
    public ImmutableHashSet<string> EndExpiredSessions()
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

        var ended = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var session in expired)
        {
            if (Change.EndSession(session))
            {
                ended.Add(session);
            }
        }

        return ended.ToImmutable();
    }
}
