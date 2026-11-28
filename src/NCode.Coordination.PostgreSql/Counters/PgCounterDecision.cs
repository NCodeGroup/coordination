// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds what a batch decided: the result to return and the rows to write.
/// </summary>
/// <param name="Result">The result of the batch, or null when a sum would pass the largest count.</param>
/// <param name="Writes">The counters to write, which are empty when <paramref name="Result"/> is null.</param>
internal sealed record PgCounterDecision(
    CounterBatchResult? Result,
    ImmutableArray<PgCounterWrite> Writes
)
{
    /// <summary>
    /// Gets the decision of a batch that has a sum past the largest count, which writes nothing.
    /// </summary>
    public static PgCounterDecision Overflow { get; } = new(null, []);
}
