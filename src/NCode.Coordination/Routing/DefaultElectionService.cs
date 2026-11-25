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
internal class DefaultElectionService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : IElectionService
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
    public ValueTask<ElectionResult> TryLeadAsync(
        CoordinationKey role,
        ReadOnlyMemory<byte> value,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);

        var target = Composition.Resolve<IElectionService>(Primitives.Election, role.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.ElectionTryLead,
            (service, token) => service.TryLeadAsync(role, value, terms, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        Leadership leadership,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);

        var role = leadership.Leader.Role;
        var target = Composition.Resolve<IElectionService>(Primitives.Election, role.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.ElectionRenew,
            (service, token) => service.RenewAsync(leadership, timeToLive, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(leadership);

        var role = leadership.Leader.Role;
        var target = Composition.Resolve<IElectionService>(Primitives.Election, role.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.ElectionResign,
            (service, token) => service.ResignAsync(leadership, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);

        var target = Composition.Resolve<IElectionService>(Primitives.Election, role.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.ElectionRead,
            (service, token) => service.ReadAsync(role, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ElectionView> WatchAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(role);

        var target = Composition.Resolve<IElectionService>(Primitives.Election, role.Keyspace);
        return Instrumentation.MeasureStreamAsync(
            target,
            Operations.ElectionWatch,
            (service, token) => service.WatchAsync(role, options, token),
            cancellationToken
        );
    }
}
