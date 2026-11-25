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
internal class DefaultSessionService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ISessionService
{
    private CoordinationComposition Composition { get; } = composition;

    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public ValueTask<SessionGrant> OpenAsync(
        Keyspace keyspace,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);

        var target = Composition.Resolve<ISessionService>(Primitives.Session, keyspace);
        return Instrumentation.MeasureAsync(target, Operations.SessionOpen, service =>
            service.OpenAsync(keyspace, timeToLive, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask<KeepAliveResult> KeepAliveAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        var target = Composition.Resolve<ISessionService>(Primitives.Session, session.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.SessionKeepAlive, service =>
            service.KeepAliveAsync(session, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask CloseAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        var target = Composition.Resolve<ISessionService>(Primitives.Session, session.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.SessionClose, service =>
            service.CloseAsync(session, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        var target = Composition.Resolve<ISessionService>(Primitives.Session, session.Keyspace);
        return Instrumentation.MeasureAsync(target, Operations.SessionWaitForEnd, service =>
            service.WaitForEndAsync(session, options, cancellationToken)
        );
    }
}
