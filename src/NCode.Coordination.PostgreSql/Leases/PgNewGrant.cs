// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds a grant to write: the slot of a resource that a new holder takes.
/// </summary>
/// <param name="Kind">The kind of the grant: 1 for leases, 2 for semaphores, 3 for elections.</param>
/// <param name="Resource">The resource that the grant guards.</param>
/// <param name="Slot">The slot the grant holds.</param>
/// <param name="Capacity">The capacity of the semaphore the grant belongs to; 1 for a lease or an election.</param>
/// <param name="Owner">The owner token of the grant.</param>
internal sealed record PgNewGrant(
    short Kind,
    CoordinationKey Resource,
    int Slot,
    int Capacity,
    string Owner
)
{
    /// <summary>
    /// Gets the id of the session the grant is bound to, or null.
    /// </summary>
    public string? Session { get; init; }

    /// <summary>
    /// Gets the instant the grant expires, or null when it is bound to a session.
    /// </summary>
    public DateTime? Deadline { get; init; }

    /// <summary>
    /// Gets the leader's value of an election, or null.
    /// </summary>
    public byte[]? Value { get; init; }
}
