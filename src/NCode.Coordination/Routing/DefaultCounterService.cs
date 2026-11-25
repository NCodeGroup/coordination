// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Composition;
using NCode.Coordination.Telemetry;

namespace NCode.Coordination.Routing;

/// <summary>
/// Routes each call to the provider bound for its key's keyspace, and traces and measures it.
/// </summary>
/// <param name="composition">Resolves the provider bound for a keyspace.</param>
/// <param name="instrumentation">Traces and measures each call.</param>
internal class DefaultCounterService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ICounterService
{
    private CoordinationComposition Composition { get; } = composition;

    private OperationInstrumentation Instrumentation { get; } = instrumentation;

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

        var target = Composition.Resolve<ICounterService>(Primitives.Counter, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.CounterIncrement, service =>
            service.IncrementAsync(key, amount, timeToLive, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<long> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ICounterService>(Primitives.Counter, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.CounterRead, service =>
            service.ReadAsync(key, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ICounterService>(Primitives.Counter, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.CounterReset, service =>
            service.ResetAsync(key, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(increments);
        if (increments.Count == 0 || increments[0] is not { } first)
        {
            throw new ArgumentException(
                "A batch has at least one increment, and its first is not null.",
                nameof(increments)
            );
        }

        var keyspace = first.Key.Keyspace;
        foreach (var increment in increments)
        {
            if (increment is null || !keyspace.Equals(increment.Key.Keyspace))
            {
                throw new ArgumentException(
                    "A batch's increments are all non-null and in one keyspace.",
                    nameof(increments)
                );
            }
        }

        var target = Composition.Resolve<ICounterService>(Primitives.Counter, keyspace);
        return Instrumentation.MeasureAsync(target, Operations.CounterIncrementBatch, service =>
            service.IncrementBatchAsync(increments, options, cancellationToken)
        );
    }
}
