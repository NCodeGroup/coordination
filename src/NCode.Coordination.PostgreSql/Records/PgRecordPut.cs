// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds a record that a keyspace change writes.
/// </summary>
/// <param name="Key">The record's key.</param>
/// <param name="Value">The record's value, which the put owns: the change stores it without copying it.</param>
/// <param name="Deadline">The instant the record expires, or null when it doesn't.</param>
/// <param name="Session">The id of the session the record is bound to, or null when it is bound to none.</param>
internal sealed record PgRecordPut(
    CoordinationKey Key,
    byte[] Value,
    DateTime? Deadline,
    string? Session
)
{
    /// <summary>
    /// Gets the id of the ended session whose row the write replaced, or null.
    /// </summary>
    public string? ReplacedSession { get; init; }
}
