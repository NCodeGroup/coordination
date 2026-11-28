// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds one member row of a group with the deadline of its session.
/// </summary>
/// <param name="Session">The id of the session the member is bound to.</param>
/// <param name="NodeId">The member's node id.</param>
/// <param name="Deadline">The deadline of the session's row, or null when the row is gone.</param>
internal sealed record PgGroupRow(string Session, string NodeId, DateTime? Deadline)
{
    /// <summary>Tells whether the member's session is live at an instant.</summary>
    /// <param name="now">The instant, on the server's clock.</param>
    /// <returns><see langword="true"/> when the session's row exists and its deadline is after the instant.</returns>
    public bool IsLiveAt(DateTime now) => Deadline is { } deadline && deadline > now;
}
