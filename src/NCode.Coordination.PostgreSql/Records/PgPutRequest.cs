// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds what a write of one record needs besides the keyspace's id: the key, the value, the lifetime and the session
/// that the lifetime binds the record to.
/// </summary>
/// <param name="Key">The record's key.</param>
/// <param name="Value">A copy of the record's value.</param>
/// <param name="Lifetime">The record's lifetime.</param>
/// <param name="Session">
/// The id of the session that the lifetime binds the record to, or null when it binds to none or to one of another
/// keyspace.
/// </param>
internal sealed record PgPutRequest(
    CoordinationKey Key,
    byte[] Value,
    RecordLifetime Lifetime,
    string? Session
)
{
    /// <summary>
    /// Gets the ids of the sessions that the write binds the record to, which the first batch reads.
    /// </summary>
    public string[] Sessions => Session is null ? [] : [Session];
}
