// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// What a subscriber reads from its queue: an entry in order, or a resync.
/// </summary>
/// <typeparam name="TEntry">The type of the log's entries.</typeparam>
/// <param name="IsResync">True when the event is a resync, false when it is an entry.</param>
/// <param name="Current">The log's number that a resync starts from, otherwise the entry's number.</param>
/// <param name="Entry">The entry of an entry event, otherwise the default value.</param>
internal readonly record struct PgLogEvent<TEntry>(bool IsResync, long Current, TEntry? Entry)
{
    /// <summary>
    /// Creates the event for an entry in order.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="number">The entry's number.</param>
    /// <returns>The event.</returns>
    public static PgLogEvent<TEntry> ForEntry(TEntry entry, long number) =>
        new(false, number, entry);

    /// <summary>
    /// Creates the event for a resync.
    /// </summary>
    /// <param name="current">The log's number that the resync starts from.</param>
    /// <returns>The event.</returns>
    public static PgLogEvent<TEntry> ForResync(long current) => new(true, current, default);
}
