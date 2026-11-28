// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds what a leave needs besides the keyspace's id: the group and the session that leaves it.
/// </summary>
/// <param name="Address">The group's key and the values that its rows are found by.</param>
/// <param name="Session">The session that leaves.</param>
internal sealed record PgLeaveRequest(PgGroupAddress Address, SessionId Session);
