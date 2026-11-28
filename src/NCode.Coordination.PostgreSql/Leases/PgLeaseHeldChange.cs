// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds what a change to a lease's grant needs besides the keyspace's id: the grant, and how to build the write when
/// the grant is still the live one.
/// </summary>
/// <param name="Grant">The grant to change.</param>
/// <param name="Write">Builds the write from the keyspace's id, the grant's row and the server's instant.</param>
internal sealed record PgLeaseHeldChange(
    LeaseGrant Grant,
    Func<long, PgGrantRow, DateTime, PgStatement> Write
);
