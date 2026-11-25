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
internal class DefaultOccurrenceService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : IOccurrenceService
{
    private CoordinationComposition Composition { get; } = composition;

    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public ValueTask<OccurrenceStatus> TryRecordAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<IOccurrenceService>(Primitives.Occurrence, key.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.OccurrenceTryRecord, service =>
            service.TryRecordAsync(key, timeToLive, options, cancellationToken)
        );
    }
}
