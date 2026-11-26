// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Recipes;

/// <summary>
/// Builds the keys a recipe keeps its own state under, one segment below the key its caller names.
/// </summary>
internal static class RecipeKeys
{
    /// <summary>
    /// Checks that a key has room for one more segment.
    /// </summary>
    /// <param name="key">The key the caller named.</param>
    /// <param name="paramName">The name of the caller's parameter that holds the key.</param>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="ArgumentException">The key already has the most segments a key holds.</exception>
    public static void ThrowIfFull(CoordinationKey key, string paramName)
    {
        ArgumentNullException.ThrowIfNull(key, paramName);

        if (key.Segments.Length >= CoordinationKey.MaxSegments)
        {
            throw new ArgumentException(
                $"A recipe adds a segment under its key, so the key has at most {CoordinationKey.MaxSegments - 1} segments.",
                paramName
            );
        }
    }

    /// <summary>
    /// Returns the key one segment below a key, in the same keyspace and with the same partition depth.
    /// </summary>
    /// <param name="key">The key the caller named.</param>
    /// <param name="segment">The segment to add.</param>
    /// <param name="paramName">The name of the caller's parameter that holds the key.</param>
    /// <returns>The key with the segment added.</returns>
    /// <exception cref="ArgumentNullException">The key or the segment is null.</exception>
    /// <exception cref="ArgumentException">The key already has the most segments a key holds.</exception>
    public static CoordinationKey Child(CoordinationKey key, KeySegment segment, string paramName)
    {
        ArgumentNullException.ThrowIfNull(key, paramName);
        ArgumentNullException.ThrowIfNull(segment);
        ThrowIfFull(key, paramName);

        return new CoordinationKey(key.Keyspace, [.. key.Segments, segment])
        {
            PartitionDepth = key.PartitionDepth,
        };
    }
}
