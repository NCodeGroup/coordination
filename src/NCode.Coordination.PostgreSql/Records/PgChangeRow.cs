// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// One row that the change log's entries statement returns: an entry's columns and, when it has one, the item the
/// row carries.
/// </summary>
/// <param name="Revision">The entry's revision.</param>
/// <param name="EndedSession">The id of the session the entry ended, or null.</param>
/// <param name="GroupIndex">The index value of the group the entry names, or null.</param>
/// <param name="GroupDepth">The partition depth of the group the entry names, or null.</param>
/// <param name="Item">The item the row carries, or null when the entry has no items.</param>
internal sealed record PgChangeRow(
    long Revision,
    string? EndedSession,
    byte[]? GroupIndex,
    short? GroupDepth,
    PgChangeItem? Item
);
