// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Elections;

/// <summary>
/// Holds what an attempt to lead a role needs besides the keyspace's id: the role, the value the leader publishes, the
/// terms of the leadership and the id of the terms' session.
/// </summary>
/// <param name="Role">The role to lead.</param>
/// <param name="Value">The value the leader publishes, which the request owns a copy of.</param>
/// <param name="Terms">The terms of the leadership.</param>
/// <param name="SessionId">The id of the terms' session, or null when the leadership is bound to none.</param>
internal sealed record PgLeadRequest(
    CoordinationKey Role,
    byte[] Value,
    LeaseTerms Terms,
    string? SessionId
);
