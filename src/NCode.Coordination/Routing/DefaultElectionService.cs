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
    private CoordinationComposition Composition { get; } = composition;

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
        return Instrumentation.MeasureAsync(target, Operations.ElectionTryLead, service =>
            service.TryLeadAsync(role, value, terms, options, cancellationToken)
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
        return Instrumentation.MeasureAsync(target, Operations.ElectionRenew, service =>
            service.RenewAsync(leadership, timeToLive, options, cancellationToken)
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
        return Instrumentation.MeasureAsync(target, Operations.ElectionResign, service =>
            service.ResignAsync(leadership, options, cancellationToken)
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
        return Instrumentation.MeasureAsync(target, Operations.ElectionRead, service =>
            service.ReadAsync(role, options, cancellationToken)
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
        return Instrumentation.MeasureStream(
            target,
            Operations.ElectionWatch,
            service => service.WatchAsync(role, options, cancellationToken),
            cancellationToken
        );
    }
}
