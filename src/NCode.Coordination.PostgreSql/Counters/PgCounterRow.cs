// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds one row of the <c>counters</c> table, as a batch's first statement returns it.
/// </summary>
/// <param name="Index">The row's index value.</param>
/// <param name="Depth">The row's partition depth.</param>
/// <param name="Count">The row's count.</param>
/// <param name="Deadline">The instant on the server's clock at which the row expires.</param>
internal sealed record PgCounterRow(byte[] Index, short Depth, long Count, DateTime Deadline)
{
    /// <summary>
    /// Gets the index value and the depth, which name the row among the rows of a batch.
    /// </summary>
    public PgRowIdentity Identity => new(Index, Depth);
}
