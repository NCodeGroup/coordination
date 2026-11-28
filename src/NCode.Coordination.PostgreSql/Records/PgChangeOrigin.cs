// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds where a keyspace change starts: the keyspace, the revision and the instant its transaction read.
/// </summary>
/// <param name="KeyspaceId">The keyspace's id.</param>
/// <param name="Revision">The keyspace's revision that the transaction read, which the first entry follows.</param>
/// <param name="Now">The instant the transaction read on the server's clock.</param>
internal sealed record PgChangeOrigin(long KeyspaceId, long Revision, DateTime Now);
