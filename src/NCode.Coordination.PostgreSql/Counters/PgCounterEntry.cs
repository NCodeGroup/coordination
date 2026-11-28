// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds the columns that identify the row of one counter of a batch.
/// </summary>
/// <param name="Key">The key of the counter.</param>
/// <param name="Index">The key's index value.</param>
/// <param name="Depth">The key's partition depth.</param>
/// <param name="Text">The key's text.</param>
internal sealed record PgCounterEntry(CoordinationKey Key, byte[] Index, short Depth, string Text)
{
    /// <summary>
    /// Gets the index value as hexadecimal text and the depth, which name the row among the rows of a batch.
    /// </summary>
    public (string Index, short Depth) Slot => (Convert.ToHexString(Index), Depth);
}
