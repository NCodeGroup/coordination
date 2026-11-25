// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A notification a subscription delivers: a message published on a topic, or a resync.
/// </summary>
/// <remarks>
/// Create a notification with <see cref="Message(CoordinationKey, long, ReadOnlyMemory{byte})"/> or
/// <see cref="Resync(CoordinationKey, long)"/>. A resync means the subscriber may have missed messages: it rereads
/// the state the topic announces.
/// </remarks>
public sealed class Notification
{
    private Notification(
        NotificationKind kind,
        CoordinationKey topic,
        long sequence,
        ReadOnlyMemory<byte> payload
    )
    {
        Kind = kind;
        Topic = topic;
        Sequence = sequence;
        Payload = payload;
    }

    /// <summary>
    /// Gets the kind of the notification.
    /// </summary>
    public NotificationKind Kind { get; }

    /// <summary>
    /// Gets the topic the notification belongs to.
    /// </summary>
    public CoordinationKey Topic { get; }

    /// <summary>
    /// Gets the number of a message in its topic, which is consecutive; for a resync, the last number delivered
    /// before it, or 0 when none.
    /// </summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets the payload of a message, or an empty payload for a resync.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Gets a value indicating whether the notification is a resync, which means the subscriber may have missed
    /// messages.
    /// </summary>
    public bool IsResync => Kind == NotificationKind.Resync;

    /// <summary>
    /// Creates the notification for a published message.
    /// </summary>
    /// <param name="topic">The topic the message was published on.</param>
    /// <param name="sequence">The number of the message in its topic, at least 1.</param>
    /// <param name="payload">The payload, at most <see cref="CoordinationLimits.MaxPayloadLength"/> bytes.</param>
    /// <returns>The message notification.</returns>
    /// <exception cref="ArgumentNullException">The topic is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sequence is less than 1.</exception>
    /// <exception cref="ArgumentException">The payload is longer than the limit.</exception>
    public static Notification Message(
        CoordinationKey topic,
        long sequence,
        ReadOnlyMemory<byte> payload
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        if (payload.Length > CoordinationLimits.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"A payload has at most {CoordinationLimits.MaxPayloadLength} bytes.",
                nameof(payload)
            );
        }

        return new Notification(NotificationKind.Message, topic, sequence, payload);
    }

    /// <summary>
    /// Creates the notification that tells the subscriber it may have missed messages.
    /// </summary>
    /// <param name="topic">The topic the subscriber may have missed messages on.</param>
    /// <param name="sequence">The last number delivered before the resync, or 0 when none.</param>
    /// <returns>The resync notification.</returns>
    /// <exception cref="ArgumentNullException">The topic is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sequence is negative.</exception>
    public static Notification Resync(CoordinationKey topic, long sequence)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        return new Notification(
            NotificationKind.Resync,
            topic,
            sequence,
            ReadOnlyMemory<byte>.Empty
        );
    }
}
