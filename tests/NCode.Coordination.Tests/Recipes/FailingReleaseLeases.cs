// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Recipes.Tests;

/// <summary>
/// A lease service that forwards every call to another, except a release, which fails as an unreachable provider does.
/// </summary>
internal sealed class FailingReleaseLeases(ILeaseService inner) : ILeaseService
{
    public ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) => inner.TryAcquireAsync(resource, terms, options, cancellationToken);

    public ValueTask<bool> RenewAsync(
        LeaseGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) => inner.RenewAsync(grant, timeToLive, options, cancellationToken);

    public ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        var failure = new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            outcomeUnknown: false,
            "The provider is unreachable."
        );

        return ValueTask.FromException<bool>(failure);
    }

    public ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) => inner.WaitForReleaseAsync(resource, options, cancellationToken);
}
