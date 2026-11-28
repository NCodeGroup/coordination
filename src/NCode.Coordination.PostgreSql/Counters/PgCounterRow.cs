// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds one row of the <c>counters</c> table, as a batch's first statement returns it.
/// </summary>
/// <param name="Index">The row's index value, as hexadecimal text.</param>
/// <param name="Depth">The row's partition depth.</param>
/// <param name="Count">The row's count.</param>
/// <param name="Deadline">The instant on the server's clock at which the row expires.</param>
internal sealed record PgCounterRow(string Index, short Depth, long Count, DateTime Deadline)
{
    /// <summary>
    /// Gets the index value and the depth, which name the row among the rows of a batch.
    /// </summary>
    public (string Index, short Depth) Slot => (Index, Depth);
}
