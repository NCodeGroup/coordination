// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Names the resource whose grant rows an operation reads, in its keyspace.
/// </summary>
/// <param name="KeyspaceId">The keyspace's id.</param>
/// <param name="Kind">The kind of the grants: 1 for leases, 2 for semaphores, 3 for elections.</param>
/// <param name="Resource">The resource that the grants guard.</param>
internal readonly record struct PgGrantScope(long KeyspaceId, short Kind, CoordinationKey Resource);
