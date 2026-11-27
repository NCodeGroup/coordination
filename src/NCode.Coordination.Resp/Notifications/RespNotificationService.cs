// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Notifications;

/// <summary>
/// The notifications of the RESP provider: a topic's sequence is a server key that never expires, and one script takes
/// its next sequence and publishes it with the payload on the topic's channel, whose name is the key's.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <param name="profile">The profile of the server.</param>
/// <param name="subscriptions">Opens the provider's subscriptions to numbered channels.</param>
/// <exception cref="ArgumentNullException">The server, the profile or the subscriptions are null.</exception>
internal class RespNotificationService(
    RespServer server,
    RespProfile profile,
    RespSubscriptions subscriptions
) : INotificationService
{
    /// <summary>
    /// The kind of server key that holds a topic's sequence.
    /// </summary>
    internal const string Kind = "topic";

    /// <summary>
    /// Reads the payload of a message: the bytes after its first line feed.
    /// </summary>
    /// <param name="message">The message that a channel delivered.</param>
    /// <param name="payload">The payload, or an empty array when the message is malformed.</param>
    /// <returns>True when the message has a line feed and a payload within the limit; otherwise false.</returns>
    internal static bool TryReadPayload(RedisValue message, out byte[] payload)
    {
        payload = [];

        var bytes = (byte[]?)message;
        if (bytes is null)
        {
            return false;
        }

        var end = Array.IndexOf(bytes, (byte)'\n');
        if (end < 0 || bytes.Length - end - 1 > CoordinationLimits.MaxPayloadLength)
        {
            return false;
        }

        payload = bytes[(end + 1)..];
        return true;
    }

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the profile of the server.
    /// </summary>
    private RespProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>
    /// Gets the object that opens the provider's subscriptions to numbered channels.
    /// </summary>
    private RespSubscriptions Subscriptions { get; } =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The payload is longer than the limit.</exception>
    public async ValueTask PublishAsync(
        CoordinationKey topic,
        ReadOnlyMemory<byte> payload,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (payload.Length > CoordinationLimits.MaxPayloadLength)
        {
            throw new ArgumentException(
                $"A payload has at most {CoordinationLimits.MaxPayloadLength} bytes.",
                nameof(payload)
            );
        }

        RespCall.Check(options, cancellationToken);

        var key = RespKeys.ForKey(topic, Kind);
        RedisKey[] keys = [key];
        RedisValue[] arguments = [key.ToString(), Profile.ShardedChannels ? "1" : "0", payload];

        var reply = await Server
            .EvaluateAsync(RespScript.NotificationPublish, keys, arguments, cancellationToken)
            .ConfigureAwait(false);

        if (RespReplies.Integer(reply) < 1)
        {
            throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Notification> SubscribeAsync(
        CoordinationKey topic,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        RespCall.Check(options, cancellationToken);

        return SubscribeCoreAsync(topic, cancellationToken);
    }

    /// <summary>
    /// Streams the notifications of a topic: a resync at the start and after each gap, then each message in order.
    /// </summary>
    /// <param name="topic">The topic to stream.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of notifications.</returns>
    private async IAsyncEnumerable<Notification> SubscribeCoreAsync(
        CoordinationKey topic,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var key = RespKeys.ForKey(topic, Kind);
        var name = key.ToString();
        var reader = await Subscriptions
            .OpenAsync(name, token => ReadSequenceAsync(key, token), cancellationToken)
            .ConfigureAwait(false);

        await using var disposal = reader.ConfigureAwait(false);

        long delivered = 0;
        await foreach (var item in reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (item.IsResync)
            {
                yield return Notification.Resync(topic, delivered);
                continue;
            }

            if (!TryReadPayload(item.Message, out var payload))
            {
                reader.MarkInDoubt();
                continue;
            }

            delivered = item.Number;
            yield return Notification.Message(topic, item.Number, payload);
        }
    }

    /// <summary>
    /// Reads the last sequence of a topic from the server.
    /// </summary>
    /// <param name="key">The server key of the topic's sequence.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The last sequence, or 0 when the topic has none.</returns>
    private async ValueTask<long> ReadSequenceAsync(
        RedisKey key,
        CancellationToken cancellationToken
    )
    {
        var reply = await Server
            .ExecuteAsync(RespCommands.Get, [key], cancellationToken)
            .ConfigureAwait(false);

        return reply.IsNull ? 0 : RespReplies.Integer(reply);
    }
}
