// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds one row of a group's read: the keyspace's revision, the server's instant and, when the group has a
/// member, the member with the deadline of its session.
/// </summary>
/// <param name="Revision">The keyspace's revision.</param>
/// <param name="Now">The server's instant.</param>
/// <param name="Session">The id of the member's session, or null when the group has no member.</param>
/// <param name="NodeId">The member's node id.</param>
/// <param name="Address">The member's address.</param>
/// <param name="Names">The names of the member's metadata, ordinally sorted.</param>
/// <param name="Values">The values of the member's metadata, in the order of the names.</param>
/// <param name="Deadline">The deadline of the member's session.</param>
internal sealed record PgMemberRow(
    long Revision,
    DateTime Now,
    string? Session,
    string? NodeId,
    string? Address,
    string[]? Names,
    string[]? Values,
    DateTime? Deadline
)
{
    /// <summary>
    /// Tells whether the member's session has passed its deadline at the instant of the read.
    /// </summary>
    /// <returns><see langword="true"/> when the row holds a member whose session's deadline has passed.</returns>
    public bool IsExpired() => Deadline is { } deadline && deadline <= Now;
}
