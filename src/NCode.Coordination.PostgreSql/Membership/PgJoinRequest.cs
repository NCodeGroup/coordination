// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// Holds what a join needs besides the keyspace's id: the group, the member and the member's row as arguments.
/// </summary>
/// <param name="Address">The group's key and the values that its rows are found by.</param>
/// <param name="Member">The member that joins.</param>
/// <param name="GroupText">The group's key as text.</param>
/// <param name="Names">The names of the member's metadata, ordinally sorted, as a statement's argument.</param>
/// <param name="Values">The values of the member's metadata, in the order of the names, as a statement's argument.</param>
internal sealed record PgJoinRequest(
    PgGroupAddress Address,
    Member Member,
    string GroupText,
    PgArgument Names,
    PgArgument Values
);
