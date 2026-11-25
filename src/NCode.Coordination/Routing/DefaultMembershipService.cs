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
internal class DefaultMembershipService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : IMembershipService
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
    public ValueTask<MembershipJoinStatus> JoinAsync(
        CoordinationKey group,
        Member member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);

        var target = Composition.Resolve<IMembershipService>(Primitives.Membership, group.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.MembershipJoin,
            (service, token) => service.JoinAsync(group, member, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask LeaveAsync(
        CoordinationKey group,
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);

        var target = Composition.Resolve<IMembershipService>(Primitives.Membership, group.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.MembershipLeave,
            (service, token) => service.LeaveAsync(group, session, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<MembershipView> ReadAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);

        var target = Composition.Resolve<IMembershipService>(Primitives.Membership, group.Keyspace);
        return Instrumentation.MeasureAsync(
            target,
            Operations.MembershipRead,
            (service, token) => service.ReadAsync(group, options, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);

        var target = Composition.Resolve<IMembershipService>(Primitives.Membership, group.Keyspace);
        return Instrumentation.MeasureStreamAsync(
            target,
            Operations.MembershipWatch,
            (service, token) => service.WatchAsync(group, options, token),
            cancellationToken
        );
    }
}
