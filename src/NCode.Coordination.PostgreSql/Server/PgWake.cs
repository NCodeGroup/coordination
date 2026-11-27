// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Holds what a <c>pg_notify</c> payload says: which log has grown and to what number.
/// </summary>
/// <param name="TopicToken">The continuation-token encoding of the topic's key, or null for the keyspace's change log.</param>
/// <param name="Number">The revision of the change log, or the sequence of the topic's message.</param>
internal readonly record struct PgWake(string? TopicToken, long Number);
