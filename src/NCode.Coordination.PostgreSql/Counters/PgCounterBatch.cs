// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds the increments of a batch and the entry of each distinct key among them.
/// </summary>
/// <param name="Increments">The increments, in the order the caller gave them.</param>
/// <param name="Entries">The entry of each distinct key, in key order, which is the order the rows are locked in.</param>
internal sealed record PgCounterBatch(
    IReadOnlyList<CounterIncrement> Increments,
    ImmutableArray<PgCounterEntry> Entries
);
