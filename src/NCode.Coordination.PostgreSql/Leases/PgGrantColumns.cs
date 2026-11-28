// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds the columns that a new grant's two statements share: its row and its resource's fencing row.
/// </summary>
/// <param name="KeyspaceId">The keyspace's id.</param>
/// <param name="Index">The index value of the grant's resource.</param>
/// <param name="Text">The text of the grant's resource.</param>
/// <param name="Fencing">The fencing token of the grant.</param>
internal sealed record PgGrantColumns(long KeyspaceId, byte[] Index, string Text, long Fencing);
