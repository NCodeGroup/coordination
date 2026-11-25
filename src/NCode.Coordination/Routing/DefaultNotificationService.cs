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
internal class DefaultNotificationService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : INotificationService
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
    public ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        var target = Composition.Resolve<INotificationService>(
            Primitives.Notification,
            topic.Keyspace
        );
        return Instrumentation.MeasureAsync(target, Operations.NotificationPublish, service =>
            service.PublishAsync(topic, payload, options, cancellationToken)
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        var target = Composition.Resolve<INotificationService>(
            Primitives.Notification,
            topic.Keyspace
        );
        return Instrumentation.MeasureStream(
            target,
            Operations.NotificationSubscribe,
            service => service.SubscribeAsync(topic, options, cancellationToken),
            cancellationToken
        );
    }
}
