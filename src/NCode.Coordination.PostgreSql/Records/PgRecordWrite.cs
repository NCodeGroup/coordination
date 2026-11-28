// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds one record write of an entry.
/// </summary>
/// <param name="Kind">1 for a record written, 2 for a record deleted.</param>
/// <param name="Index">The record's index value.</param>
/// <param name="Depth">The record's partition depth.</param>
/// <param name="Key">The record's key text.</param>
/// <param name="Value">The value written, or null for a deletion.</param>
/// <param name="Deadline">The instant the record written expires, or null.</param>
/// <param name="Session">The session the record written is bound to, or null.</param>
/// <param name="ReplacedSession">The ended session whose row the write replaced, or null.</param>
internal sealed record PgRecordWrite(
    short Kind,
    byte[] Index,
    short Depth,
    string Key,
    byte[]? Value,
    DateTime? Deadline,
    string? Session,
    string? ReplacedSession
);
