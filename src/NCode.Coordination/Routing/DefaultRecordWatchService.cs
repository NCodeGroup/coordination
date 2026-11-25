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
internal class DefaultRecordWatchService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : IRecordWatchService
{
    private CoordinationComposition Composition { get; } = composition;

    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IRecordWatchService>(Primitives.RecordWatch, key.Keyspace);
        return Instrumentation.MeasureStream(
            target,
            Operations.RecordWatchKey,
            service => service.WatchKeyAsync(key, after, options, cancellationToken),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var target = Composition.Resolve<IRecordWatchService>(
            Primitives.RecordWatch,
            prefix.Keyspace
        );
        return Instrumentation.MeasureStream(
            target,
            Operations.RecordWatchPrefix,
            service => service.WatchPrefixAsync(prefix, after, options, cancellationToken),
            cancellationToken
        );
    }
}
