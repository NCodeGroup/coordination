// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A block of consecutive numbers reserved from a sequence.
/// </summary>
public sealed class SequenceBlock
{
    /// <summary>
    /// Creates a block.
    /// </summary>
    /// <param name="first">The first number of the block, at least 1.</param>
    /// <param name="count">The count of numbers in the block, from 1 to the largest block a provider reserves.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The first number is below 1, the count is outside the allowed range, or the block would pass the largest number.
    /// </exception>
    public SequenceBlock(long first, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, CoordinationLimits.MaxSequenceBlock);

        if (first > long.MaxValue - (count - 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(first),
                "A block ends at or below the largest number a sequence holds."
            );
        }

        First = first;
        Count = count;
    }

    /// <summary>
    /// Gets the first number of the block.
    /// </summary>
    public long First { get; }

    /// <summary>
    /// Gets the count of numbers in the block.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the last number of the block.
    /// </summary>
    public long Last => First + Count - 1;
}
