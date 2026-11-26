// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Tells every node's cache which entries changed, over a notification topic.
/// </summary>
/// <remarks>
/// An invalidation names an entry, not its data. The subscriber invalidates the named entry, clears the cache on a
/// resync, which tells it that it may have missed an invalidation, and clears it whenever
/// <see cref="RunAsync"/> stops. <see cref="RunAsync"/> ends only by cancellation or failure.
/// </remarks>
public interface ICacheInvalidationService
{
    /// <summary>
    /// Publishes that an entry changed.
    /// </summary>
    /// <remarks>
    /// The entry is at most <see cref="CoordinationLimits.MaxPayloadLength"/> bytes.
    /// </remarks>
    /// <param name="topic">The topic the caches of the nodes subscribe to.</param>
    /// <param name="entry">The identity of the entry that changed.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the provider has accepted the invalidation.</returns>
    ValueTask InvalidateAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> entry,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Keeps a cache free of stale entries until the token is cancelled or the provider fails.
    /// </summary>
    /// <remarks>
    /// The method clears the cache when the subscription starts and on every resync, invalidates the entry of every
    /// message, and clears the cache once more when it stops, so a cache that stops hearing invalidations is empty.
    /// </remarks>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="handler">The cache to keep free of stale entries.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that stops the subscription.</param>
    /// <returns>A task that ends by cancellation or by the failure of the provider.</returns>
    ValueTask RunAsync(
        CoordinationKey topic,
        ICacheInvalidationHandler handler,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
