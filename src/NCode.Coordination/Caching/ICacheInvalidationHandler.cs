// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The cache that the application keeps, which a cache invalidation subscriber keeps free of stale entries.
/// </summary>
/// <remarks>
/// Both methods may run concurrently with the application's reads of the cache.
/// </remarks>
public interface ICacheInvalidationHandler
{
    /// <summary>
    /// Removes one entry from the cache.
    /// </summary>
    /// <param name="entry">The identity of the entry that changed, as its publisher named it.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the entry is removed.</returns>
    ValueTask InvalidateAsync(ReadOnlyMemory<byte> entry, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry from the cache.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the cache is empty.</returns>
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
