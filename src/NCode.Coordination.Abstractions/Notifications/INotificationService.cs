// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of notifications: signals on a topic, numbered per topic, with a resync whenever a subscriber may have missed one.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface INotificationService
{
    /// <summary>
    /// Publishes a notification on a topic.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.NotificationPublish"/>. The payload is at most
    /// <see cref="CoordinationLimits.MaxPayloadLength"/> bytes and names what changed, never the state itself. The provider reads the payload before the returned task completes, so the caller may
    /// reuse its buffer once it does.
    /// </remarks>
    /// <param name="topic">The topic to publish on.</param>
    /// <param name="payload">The payload that names what changed.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the provider has accepted the notification.</returns>
    ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Subscribes to the notifications of a topic.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.NotificationSubscribe"/>. The stream starts with a resync, then delivers
    /// each message at most once, in sequence order. It delivers a resync whenever the subscriber may have missed a
    /// message, after a gap or a reconnect. A provider that cannot number messages declares
    /// <see cref="Guarantees.AtMostOnce"/> rather than <see cref="Guarantees.AtMostOnceWithGapDetection"/>. The
    /// stream ends only when it is cancelled or when the provider fails.
    /// </remarks>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The notifications of the topic.</returns>
    IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
