// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds what an attempt to acquire a lease needs besides the keyspace's id: the resource, the terms of the lease and
/// the id of the terms' session.
/// </summary>
/// <param name="Resource">The resource that the lease guards.</param>
/// <param name="Terms">The terms of the lease.</param>
/// <param name="SessionId">The id of the terms' session, or null when the lease is bound to none.</param>
internal sealed record PgLeaseRequest(
    CoordinationKey Resource,
    LeaseTerms Terms,
    string? SessionId
);
