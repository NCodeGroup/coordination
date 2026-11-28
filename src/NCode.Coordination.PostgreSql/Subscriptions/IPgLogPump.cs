// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Subscriptions;

/// <summary>
/// Reads a log once for all of its subscribers, without regard to the type of its entries.
/// </summary>
internal interface IPgLogPump
{
    /// <summary>
    /// Gets the number of subscribers.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Reads the log after the lowest cursor of the subscribers and hands each its entries or a resync.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>A task that completes when every subscriber has what the log holds.</returns>
    ValueTask PumpAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks that a wake-up arrived.
    /// </summary>
    /// <returns>
    /// True when no wake-up was waiting for a pump, so the caller starts one; false when a pump that hasn't begun its
    /// read will cover this wake-up.
    /// </returns>
    bool TryMarkWake();

    /// <summary>
    /// Marks every subscriber as needing a resync, which the next pump delivers.
    /// </summary>
    void ResyncAll();
}
