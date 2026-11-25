// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The counters of the in-process provider.
/// </summary>
/// <param name="time">The clock the counters' expiries are measured on.</param>
/// <remarks>
/// Every operation runs entirely while holding the service's lock, so it is linearizable in the process and a batch
/// is atomic across partitions.
/// </remarks>
internal class InProcessCounterService(TimeProvider time) : ICounterService
{
    /// <summary>
    /// Creates the exception of a count that would pass the largest count.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static CoordinationException CreateOverflow()
    {
        return new CoordinationException(
            CoordinationErrorCodes.LimitExceeded,
            outcomeUnknown: false,
            "The counter would pass the largest count."
        );
    }

    /// <summary>
    /// Gets the lock that every operation holds.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the clock the counters' expiries are measured on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the counts, each held until its time to live ends.
    /// </summary>
    private InProcessExpiringTable<CoordinationKey, long> Counters { get; } = new(time);

    /// <inheritdoc />
    public ValueTask<long> IncrementAsync(
        CoordinationKey key,
        long amount,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            if (Counters.TryGet(key, out var count))
            {
                if (count > long.MaxValue - amount)
                {
                    throw CreateOverflow();
                }

                count += amount;
                Counters.Update(key, count);
                return ValueTask.FromResult(count);
            }

            var expiry = InProcessExpiry.Begin(Time, timeToLive);
            Counters.Set(key, amount, expiry);
            return ValueTask.FromResult(amount);
        }
    }

    /// <inheritdoc />
    public ValueTask<long> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            var current = Counters.TryGet(key, out var count) ? count : 0;
            return ValueTask.FromResult(current);
        }
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            Counters.Remove(key);
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(increments);

        if (increments.Count < 1 || increments.Count > CoordinationLimits.MaxBatchIncrements)
        {
            throw new ArgumentException(
                $"A batch has 1 to {CoordinationLimits.MaxBatchIncrements} increments.",
                nameof(increments)
            );
        }

        Keyspace? keyspace = null;
        foreach (var increment in increments)
        {
            if (increment is null)
            {
                throw new ArgumentException("A batch holds no null increment.", nameof(increments));
            }

            keyspace ??= increment.Key.Keyspace;
            if (increment.Key.Keyspace != keyspace)
            {
                throw new ArgumentException(
                    "Every increment of a batch is in one keyspace.",
                    nameof(increments)
                );
            }
        }

        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            var pending = new Dictionary<CoordinationKey, long>();
            var timeToLives = new Dictionary<CoordinationKey, TimeToLive>();
            var counts = new List<long>(increments.Count);
            int? exceeded = null;

            for (var index = 0; index < increments.Count; index++)
            {
                var increment = increments[index];
                timeToLives.TryAdd(increment.Key, increment.TimeToLive);

                long current;
                if (
                    !pending.TryGetValue(increment.Key, out current)
                    && !Counters.TryGet(increment.Key, out current)
                )
                {
                    current = 0;
                }

                if (current > long.MaxValue - increment.Amount)
                {
                    throw CreateOverflow();
                }

                var newCount = current + increment.Amount;
                pending[increment.Key] = newCount;
                counts.Add(newCount);

                if (newCount > increment.Limit)
                {
                    exceeded = index;
                    break;
                }
            }

            foreach (var (key, count) in pending)
            {
                if (Counters.TryGet(key, out _))
                {
                    Counters.Update(key, count);
                }
                else
                {
                    var expiry = InProcessExpiry.Begin(Time, timeToLives[key]);
                    Counters.Set(key, count, expiry);
                }
            }

            var result = new CounterBatchResult(counts) { ExceededIndex = exceeded };
            return ValueTask.FromResult(result);
        }
    }
}
