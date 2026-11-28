// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Notifications;

/// <summary>
/// Holds the address of a topic's log: the topic's index value and depth, in its keyspace.
/// </summary>
/// <param name="KeyspaceId">The keyspace's id.</param>
/// <param name="Index">The topic's index value.</param>
/// <param name="Depth">The topic's partition depth.</param>
internal sealed record PgTopicAddress(long KeyspaceId, byte[] Index, short Depth);
