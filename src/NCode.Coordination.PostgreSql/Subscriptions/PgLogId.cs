// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// Identifies one log that subscribers read: a keyspace's change log or one of its topics' message logs.
/// </summary>
/// <param name="KeyspaceId">The id of the keyspace's row, which names the channel that wakes the log's subscribers.</param>
/// <param name="TopicToken">
/// The continuation-token encoding of the topic's key, or null for the keyspace's change log.
/// </param>
internal readonly record struct PgLogId(long KeyspaceId, string? TopicToken);
