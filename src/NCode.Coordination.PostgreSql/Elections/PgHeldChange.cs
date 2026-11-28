// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Elections;

/// <summary>
/// Holds what a change to a leadership's grant needs besides the keyspace's id: the leadership, and how to build the
/// write when the leadership is still the live one.
/// </summary>
/// <param name="Leadership">The leadership to change.</param>
/// <param name="Write">Builds the write from the keyspace's id, the grant's row and the server's instant.</param>
internal sealed record PgHeldChange(
    Leadership Leadership,
    Func<long, PgGrantRow, DateTime, PgStatement> Write
);
