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
internal class DefaultLeaseService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ILeaseService
{
    private CoordinationComposition Composition { get; } = composition;

    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        var target = Composition.Resolve<ILeaseService>(Primitives.Lease, resource.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.LeaseTryAcquire, service =>
            service.TryAcquireAsync(resource, terms, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        LeaseGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        var target = Composition.Resolve<ILeaseService>(Primitives.Lease, grant.Resource.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.LeaseRenew, service =>
            service.RenewAsync(grant, timeToLive, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        var target = Composition.Resolve<ILeaseService>(Primitives.Lease, grant.Resource.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.LeaseRelease, service =>
            service.ReleaseAsync(grant, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        var target = Composition.Resolve<ILeaseService>(Primitives.Lease, resource.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.LeaseWaitForRelease, service =>
            service.WaitForReleaseAsync(resource, options, cancellationToken)
        );
    }
}
