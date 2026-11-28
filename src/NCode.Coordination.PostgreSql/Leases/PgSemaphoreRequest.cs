// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds what an attempt to acquire a semaphore's slot needs besides the keyspace's id: the resource, the
/// semaphore's capacity, the terms of the grant and the id of the terms' session.
/// </summary>
/// <param name="Resource">The resource that the semaphore guards.</param>
/// <param name="Capacity">The semaphore's capacity.</param>
/// <param name="Terms">The terms of the grant.</param>
/// <param name="SessionId">The id of the terms' session, or null when the grant is bound to none.</param>
internal sealed record PgSemaphoreRequest(
    CoordinationKey Resource,
    int Capacity,
    LeaseTerms Terms,
    string? SessionId
);
