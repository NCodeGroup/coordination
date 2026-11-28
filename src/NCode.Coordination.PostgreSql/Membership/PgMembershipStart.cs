// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds what the first batch of a join or a leave read.
/// </summary>
/// <param name="Change">The change that the transaction builds.</param>
/// <param name="Session">The id of the session that joins or leaves.</param>
/// <param name="Rows">The group's rows.</param>
/// <param name="SessionDeadline">The deadline of the session's row, or null when it does not exist.</param>
internal sealed record PgMembershipStart(
    PgKeyspaceChange Change,
    string Session,
    ImmutableArray<PgGroupRow> Rows,
    DateTime? SessionDeadline
)
{
    /// <summary>
    /// Ends every session that the read met past its deadline: those of the group's members and the session that
    /// joins or leaves.
    /// </summary>
    public void EndExpiredSessions()
    {
        var now = Change.Now;
        SortedSet<string>? expired = null;

        foreach (var row in Rows)
        {
            if (row.Deadline is { } deadline && deadline <= now)
            {
                expired ??= new SortedSet<string>(StringComparer.Ordinal);
                expired.Add(row.Session);
            }
        }

        if (SessionDeadline is { } own && own <= now)
        {
            expired ??= new SortedSet<string>(StringComparer.Ordinal);
            expired.Add(Session);
        }

        if (expired is null)
        {
            return;
        }

        foreach (var session in expired)
        {
            Change.EndSession(session);
        }
    }
}
