// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Composition;
using NCode.Coordination.Telemetry;

namespace NCode.Coordination.Routing;

/// <summary>
/// Routes each call to the provider bound for its key's keyspace, and traces and measures it.
/// </summary>
/// <param name="composition">Resolves the provider bound for a keyspace.</param>
/// <param name="instrumentation">Traces and measures each call.</param>
internal class DefaultSetService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ISetService
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
    public ValueTask<bool> AddAsync(
        CoordinationKey key,
        KeySegment member,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ISetService>(Primitives.Set, key.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.SetAdd,
            (service, token) => service.AddAsync(key, member, timeToLive, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(
        CoordinationKey key,
        KeySegment member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ISetService>(Primitives.Set, key.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.SetRemove,
            (service, token) => service.RemoveAsync(key, member, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ImmutableArray<KeySegment>> ListAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ISetService>(Primitives.Set, key.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.SetList,
            (service, token) => service.ListAsync(key, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var target = Composition.Resolve<ISetService>(Primitives.Set, key.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.SetClear,
            (service, token) => service.ClearAsync(key, options, token),
            cancellationToken
        );
    }
}
