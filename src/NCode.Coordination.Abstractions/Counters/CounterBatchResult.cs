// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The outcome of a batch of increments.
/// </summary>
public sealed class CounterBatchResult
{
    /// <summary>
    /// Creates a batch result.
    /// </summary>
    /// <param name="counts">The count after each applied increment, in order.</param>
    /// <exception cref="ArgumentNullException">The counts are null.</exception>
    /// <exception cref="ArgumentException">The counts are empty.</exception>
    public CounterBatchResult(IEnumerable<long> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        Counts = [.. counts];

        if (Counts.Length == 0)
        {
            throw new ArgumentException(
                "A batch result has a count for each applied increment.",
                nameof(counts)
            );
        }
    }

    /// <summary>
    /// Gets the count after each applied increment, in order.
    /// </summary>
    public ImmutableArray<long> Counts { get; }

    /// <summary>
    /// Gets the index of the increment whose count then exceeded its limit, after which the batch stopped, or null
    /// when none did, which is the default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is neither null nor the index of the last count.</exception>
    public int? ExceededIndex
    {
        get;
        init
        {
            if (value is not null && value != Counts.Length - 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ExceededIndex),
                    "The exceeded index is the index of the last count, or null."
                );
            }

            field = value;
        }
    }
}
