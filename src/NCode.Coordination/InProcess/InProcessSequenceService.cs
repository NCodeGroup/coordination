// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The sequences of the in-process provider.
/// </summary>
/// <remarks>
/// Every operation runs entirely while holding the service's lock, so it is linearizable in the process. A sequence
/// is held until the process ends, because its numbers never reset.
/// </remarks>
internal class InProcessSequenceService : ISequenceService
{
    /// <summary>
    /// Creates the exception of a sequence whose numbers are used up.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static CoordinationException CreateOverflow()
    {
        return new CoordinationException(
            CoordinationErrorCodes.LimitExceeded,
            outcomeUnknown: false,
            "The sequence has handed out its largest number."
        );
    }

    /// <summary>
    /// Gets the lock that every operation holds.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the last number each sequence handed out.
    /// </summary>
    private Dictionary<CoordinationKey, long> Last { get; } = [];

    /// <summary>
    /// Sets the last number a sequence handed out.
    /// </summary>
    /// <remarks>
    /// Tests use this to reach the largest number, which a block of at most
    /// <see cref="CoordinationLimits.MaxSequenceBlock" /> numbers cannot reach.
    /// </remarks>
    /// <param name="key">The key of the sequence.</param>
    /// <param name="last">The last number the sequence handed out.</param>
    internal void SetLast(CoordinationKey key, long last)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (Gate)
        {
            Last[key] = last;
        }
    }

    /// <inheritdoc />
    public ValueTask<long> NextAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            var last = Last.GetValueOrDefault(key);
            if (last == long.MaxValue)
            {
                throw CreateOverflow();
            }

            var next = last + 1;
            Last[key] = next;
            return ValueTask.FromResult(next);
        }
    }

    /// <inheritdoc />
    public ValueTask<SequenceBlock> ReserveAsync(
        CoordinationKey key,
        int count,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, CoordinationLimits.MaxSequenceBlock);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            var last = Last.GetValueOrDefault(key);
            if (last > long.MaxValue - count)
            {
                throw CreateOverflow();
            }

            Last[key] = last + count;
            var block = new SequenceBlock(last + 1, count);
            return ValueTask.FromResult(block);
        }
    }
}
