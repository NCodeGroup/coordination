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
internal class DefaultRecordService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : IRecordService
{
    /// <summary>
    /// Gets the composition that resolves the provider bound for a key's keyspace.
    /// </summary>
    private CoordinationComposition Composition { get; } = composition;

    /// <summary>
    /// Gets the instrumentation that traces and measures each call.
    /// </summary>
    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordCreate, service =>
            service.CreateAsync(key, value, lifetime, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordRead, service =>
            service.ReadAsync(key, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordCompareAndSwap, service =>
            service.CompareAndSwapAsync(key, expected, value, lifetime, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordCompareAndDelete, service =>
            service.CompareAndDeleteAsync(key, expected, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordTake, service =>
            service.TakeAsync(key, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var target = Composition.Resolve<IRecordService>(Primitives.Record, prefix.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.RecordList, service =>
            service.ListAsync(prefix, page, options, cancellationToken)
        );
    }
}
