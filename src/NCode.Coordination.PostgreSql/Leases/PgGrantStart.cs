// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds what the first batch of a change to a resource's grants read: the change that the transaction builds, the
/// resource's grant rows, the last fencing token the resource was given and the deadline of the terms' session.
/// </summary>
/// <param name="Change">The change, built from the keyspace's revision and the server's instant.</param>
/// <param name="Rows">The resource's grant rows, expired or not, by slot.</param>
/// <param name="Last">The last fencing token given to the resource, or 0 when it has been given none.</param>
/// <param name="SessionDeadline">The deadline of the terms' session, or null when there is none or its row is gone.</param>
internal sealed record PgGrantStart(
    PgKeyspaceChange Change,
    ImmutableArray<PgGrantRow> Rows,
    long Last,
    DateTime? SessionDeadline
)
{
    /// <summary>
    /// Tells whether the session of a grant's terms has ended: the terms bind to a session whose row is gone or past
    /// its deadline.
    /// </summary>
    /// <param name="session">The id of the terms' session, or null when they bind to none.</param>
    /// <returns><see langword="true"/> when the terms bind to a session that is not live.</returns>
    public bool SessionEnded(string? session) =>
        session is not null && (SessionDeadline is not { } deadline || deadline <= Change.Now);

    /// <summary>
    /// Finds the grant row of a slot that is live at the instant of the read.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The row, or null when no live grant holds the slot.</returns>
    public PgGrantRow? LiveIn(int slot)
    {
        var now = Change.Now;

        foreach (var row in Rows)
        {
            if (row.Slot == slot && row.StateAt(now) == PgRecordState.Live)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Ends every session that the read met past its deadline, whether a grant is bound to it or it is the terms'.
    /// </summary>
    /// <param name="session">The id of the terms' session, or null when there is none.</param>
    public void EndExpiredSessions(string? session)
    {
        var now = Change.Now;
        var expired = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var row in Rows)
        {
            if (row.Session is { } bound && row.SessionDeadline is { } deadline && deadline <= now)
            {
                expired.Add(bound);
            }
        }

        if (session is not null && SessionDeadline is { } own && own <= now)
        {
            expired.Add(session);
        }

        foreach (var ended in expired)
        {
            Change.EndSession(ended);
        }
    }
}
