// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// What one snapshot of a log holds: the highest number removed, the current number and the entries after a cursor.
/// </summary>
/// <typeparam name="TEntry">The type of the log's entries.</typeparam>
/// <param name="Removed">The highest number the sweep removed from the log.</param>
/// <param name="Current">The number of the log's latest entry.</param>
/// <param name="Entries">The entries after the cursor that was read from, in number order, up to a page.</param>
internal readonly record struct PgLogRead<TEntry>(
    long Removed,
    long Current,
    ImmutableArray<TEntry> Entries
);
