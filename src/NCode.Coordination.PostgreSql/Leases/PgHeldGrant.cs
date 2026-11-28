// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds the grant that a caller holds: the tokens that make it the live one of its slot.
/// </summary>
/// <param name="Kind">The kind of the grant: 1 for leases, 2 for semaphores, 3 for elections.</param>
/// <param name="Resource">The resource that the grant guards.</param>
/// <param name="Slot">The slot the grant holds.</param>
/// <param name="Fencing">The fencing token of the grant.</param>
/// <param name="Owner">The owner token of the grant.</param>
internal sealed record PgHeldGrant(
    short Kind,
    CoordinationKey Resource,
    int Slot,
    long Fencing,
    string Owner
);
