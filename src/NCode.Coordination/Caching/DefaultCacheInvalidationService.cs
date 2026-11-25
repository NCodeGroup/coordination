// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Caching;

/// <summary>
/// Publishes cache invalidations on a notification topic and keeps a cache free of stale entries by subscribing to it.
/// </summary>
/// <param name="notifications">The notification operations the cache invalidation is built on.</param>
internal class DefaultCacheInvalidationService(INotificationService notifications)
    : ICacheInvalidationService
{
    private INotificationService Notifications { get; } = notifications;

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> entry,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        await Notifications
            .PublishAsync(topic, entry, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RunAsync(
        CoordinationKey topic,
        ICacheInvalidationHandler handler,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        try
        {
            await foreach (
                var notification in Notifications
                    .SubscribeAsync(topic, options, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (notification.IsResync)
                {
                    // The subscriber may have missed an invalidation, so no entry can be trusted.
                    await handler.ClearAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await handler
                        .InvalidateAsync(notification.Payload, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("The subscription ended without a failure.");
        }
        finally
        {
            // A cache that stops hearing invalidations is cleared.
            await handler.ClearAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
