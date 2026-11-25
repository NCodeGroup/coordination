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
    private CoordinationComposition Composition { get; } = composition;

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
        return Instrumentation.MeasureAsync(target, Operations.MembershipJoin, service =>
            service.JoinAsync(group, member, options, cancellationToken)
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
        return Instrumentation.MeasureAsync(target, Operations.MembershipLeave, service =>
            service.LeaveAsync(group, session, options, cancellationToken)
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
        return Instrumentation.MeasureAsync(target, Operations.MembershipRead, service =>
            service.ReadAsync(group, options, cancellationToken)
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
        return Instrumentation.MeasureStream(
            target,
            Operations.MembershipWatch,
            service => service.WatchAsync(group, options, cancellationToken),
            cancellationToken
        );
    }
}
