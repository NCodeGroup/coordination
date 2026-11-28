// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds one row of the <c>grants</c> table, as a read returns it, beside the deadline of the session it is bound to.
/// </summary>
/// <param name="Slot">The slot the grant holds: 0 for a lease or an election, from 0 for a semaphore.</param>
/// <param name="Capacity">The capacity of the semaphore the grant belongs to; 1 for a lease or an election.</param>
/// <param name="Fencing">The fencing token the grant was made with.</param>
/// <param name="Owner">The owner token of the grant.</param>
/// <param name="Session">The id of the session the grant is bound to, or null when it has a time to live.</param>
/// <param name="Deadline">The instant on the server's clock at which the grant expires, or null when it is bound to a session.</param>
/// <param name="Value">The leader's value of an election, or null.</param>
/// <param name="SessionDeadline">The deadline of the session's row, or null when the grant is bound to none or the row is gone.</param>
internal sealed record PgGrantRow(
    int Slot,
    int Capacity,
    long Fencing,
    string Owner,
    string? Session,
    DateTime? Deadline,
    byte[]? Value,
    DateTime? SessionDeadline
)
{
    /// <summary>
    /// Tells whether the grant is bound to a session.
    /// </summary>
    /// <param name="session">The id of the session, or null when there is none.</param>
    /// <returns><see langword="true"/> when <paramref name="session"/> is not null and is the grant's session.</returns>
    public bool IsBoundTo(string? session) =>
        session is not null && string.Equals(Session, session, StringComparison.Ordinal);

    /// <summary>
    /// Gets the state of the grant at an instant.
    /// </summary>
    /// <param name="now">The instant, on the server's clock.</param>
    /// <returns>The state that <see cref="PgLiveness.StateOf"/> decides.</returns>
    public PgRecordState StateAt(DateTime now) =>
        PgLiveness.StateOf(Deadline, Session, SessionDeadline, now);
}
