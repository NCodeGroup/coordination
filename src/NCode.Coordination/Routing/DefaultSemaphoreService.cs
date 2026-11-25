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
internal class DefaultSemaphoreService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ISemaphoreService
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
    public ValueTask<SemaphoreAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        int capacity,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);

        var target = Composition.Resolve<ISemaphoreService>(
            Primitives.Semaphore,
            resource.Keyspace
        );
        return Instrumentation.MeasureAsync(
            target,
            Operations.SemaphoreTryAcquire,
            (service, token) => service.TryAcquireAsync(resource, capacity, terms, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        SemaphoreGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        var target = Composition.Resolve<ISemaphoreService>(
            Primitives.Semaphore,
            grant.Resource.Keyspace
        );
        return Instrumentation.MeasureAsync(
            target,
            Operations.SemaphoreRenew,
            (service, token) => service.RenewAsync(grant, timeToLive, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<bool> ReleaseAsync(
        SemaphoreGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        var target = Composition.Resolve<ISemaphoreService>(
            Primitives.Semaphore,
            grant.Resource.Keyspace
        );
        return Instrumentation.MeasureAsync(
            target,
            Operations.SemaphoreRelease,
            (service, token) => service.ReleaseAsync(grant, options, token),
            cancellationToken
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

        var target = Composition.Resolve<ISemaphoreService>(
            Primitives.Semaphore,
            resource.Keyspace
        );
        return Instrumentation.MeasureAsync(
            target,
            Operations.SemaphoreWaitForRelease,
            (service, token) => service.WaitForReleaseAsync(resource, options, token),
            cancellationToken
        );
    }
}
