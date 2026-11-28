// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds the keyspace, keys and sessions whose rows a change of records reads first.
/// </summary>
/// <param name="KeyspaceId">The keyspace's id.</param>
/// <param name="Keys">The keys whose rows to read; a key that is listed twice is read once.</param>
/// <param name="Sessions">The ids of the sessions that the writes bind to, whose rows to read.</param>
internal readonly record struct PgRecordScope(
    long KeyspaceId,
    ImmutableArray<CoordinationKey> Keys,
    ImmutableArray<string> Sessions
);
