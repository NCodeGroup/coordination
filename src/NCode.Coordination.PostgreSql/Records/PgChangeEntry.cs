// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds one entry of the change log: its revision and its writes.
/// </summary>
/// <param name="Revision">The entry's revision.</param>
internal sealed record PgChangeEntry(long Revision)
{
    /// <summary>
    /// Gets the entry's record writes, in order.
    /// </summary>
    public List<PgRecordWrite> Writes { get; } = [];

    /// <summary>
    /// Gets or sets the id of the session the entry ended, or null.
    /// </summary>
    public string? EndedSession { get; internal set; }

    /// <summary>
    /// Gets or sets the index value of the group the entry names, or null.
    /// </summary>
    public byte[]? GroupIndex { get; internal set; }

    /// <summary>
    /// Gets or sets the partition depth of the group the entry names, or null.
    /// </summary>
    public short? GroupDepth { get; internal set; }
}
