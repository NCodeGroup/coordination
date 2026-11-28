// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds the read-only transaction of a log read and the keyspace it reads.
/// </summary>
/// <param name="Transaction">The transaction that holds the snapshot.</param>
/// <param name="KeyspaceId">The keyspace's id.</param>
internal readonly record struct PgLogSnapshot(PgTransaction Transaction, long KeyspaceId);
