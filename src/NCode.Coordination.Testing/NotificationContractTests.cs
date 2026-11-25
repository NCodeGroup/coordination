// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the notification primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
/// <remarks>
/// A provider registers a subscription before it yields the subscription's first item, so a test publishes after it
/// reads that item and expects the message.
/// </remarks>
public abstract class NotificationContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's notification service, and skips the test when the provider does not support notifications.
    /// </summary>
    private INotificationService Notifications =>
        GetService<INotificationService>(Primitives.Notification);

    #region SubscribeAsync Tests

    /// <summary>
    /// Checks that a subscription starts with a resync of its topic that carries sequence 0.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_WhenStarted_YieldsAResyncFirst()
    {
        var topic = CreateKey("topic");

        var subscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(subscription);
        var first = await ContractStreams.NextAsync(stream);

        Assert.True(first.IsResync);
        Assert.Equal(topic, first.Topic);
        Assert.Equal(0, first.Sequence);
    }

    /// <summary>
    /// Checks that subscribing with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var topic = CreateKey("topic");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var stream = Notifications
                .SubscribeAsync(topic, null, canceledToken)
                .GetAsyncEnumerator(canceledToken);
            await stream.MoveNextAsync();
        });
    }

    #endregion

    #region PublishAsync Tests

    /// <summary>
    /// Checks that a subscriber receives a message that is published after its subscription started.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenSubscribed_DeliversTheMessage()
    {
        var topic = CreateKey("topic");
        var subscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(subscription);
        await ContractStreams.NextAsync(stream);

        var payload = "a"u8.ToArray();

        await Notifications.PublishAsync(topic, payload, null, TestCancellationToken);

        var message = await ContractStreams.NextAsync(stream);
        var delivered = message.Payload.ToArray();
        Assert.False(message.IsResync);
        Assert.Equal(topic, message.Topic);
        Assert.Equal(payload, delivered);
    }

    /// <summary>
    /// Checks that two messages published on a topic are numbered consecutively.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenPublishedTwice_NumbersTheMessagesConsecutively()
    {
        var declaresNumbering = Declares(
            Primitives.Notification,
            Guarantees.AtMostOnceWithGapDetection
        );
        Assert.SkipUnless(declaresNumbering, "The provider does not number a topic's messages.");

        var topic = CreateKey("topic");
        var subscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(subscription);
        await ContractStreams.NextAsync(stream);

        var payloadA = "a"u8.ToArray();
        var payloadB = "b"u8.ToArray();

        await Notifications.PublishAsync(topic, payloadA, null, TestCancellationToken);
        await Notifications.PublishAsync(topic, payloadB, null, TestCancellationToken);

        var first = await ContractStreams.NextAsync(stream);
        var second = await ContractStreams.NextAsync(stream);
        var firstDelivered = first.Payload.ToArray();
        var secondDelivered = second.Payload.ToArray();
        Assert.Equal(first.Sequence + 1, second.Sequence);
        Assert.Equal(payloadA, firstDelivered);
        Assert.Equal(payloadB, secondDelivered);
    }

    /// <summary>
    /// Checks that a message is delivered to every subscriber of its topic.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenTwoSubscribe_DeliversToEach()
    {
        var topic = CreateKey("topic");
        var firstSubscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var first = ContractStreams.Open(firstSubscription);
        var secondSubscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var second = ContractStreams.Open(secondSubscription);
        await ContractStreams.NextAsync(first);
        await ContractStreams.NextAsync(second);

        var payload = "a"u8.ToArray();

        await Notifications.PublishAsync(topic, payload, null, TestCancellationToken);

        var firstMessage = await ContractStreams.NextAsync(first);
        var secondMessage = await ContractStreams.NextAsync(second);
        var firstDelivered = firstMessage.Payload.ToArray();
        var secondDelivered = secondMessage.Payload.ToArray();
        Assert.Equal(payload, firstDelivered);
        Assert.Equal(payload, secondDelivered);
    }

    /// <summary>
    /// Checks that a subscriber receives only the messages of its own topic.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenTopicsDiffer_DeliversOnlyTheTopicsMessages()
    {
        var topicA = CreateKey("a");
        var topicB = CreateKey("b");
        var subscription = Notifications.SubscribeAsync(topicA, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(subscription);
        await ContractStreams.NextAsync(stream);

        var payloadA = "a"u8.ToArray();
        var payloadB = "b"u8.ToArray();

        await Notifications.PublishAsync(topicB, payloadB, null, TestCancellationToken);
        await Notifications.PublishAsync(topicA, payloadA, null, TestCancellationToken);

        var message = await ContractStreams.NextAsync(stream);
        var delivered = message.Payload.ToArray();
        Assert.False(message.IsResync);
        Assert.Equal(topicA, message.Topic);
        Assert.Equal(payloadA, delivered);
    }

    /// <summary>
    /// Checks that publishing a payload longer than the limit throws.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenPayloadIsTooLong_ThrowsArgumentException()
    {
        var topic = CreateKey("topic");
        var payload = new byte[CoordinationLimits.MaxPayloadLength + 1];

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await Notifications.PublishAsync(topic, payload, null, TestCancellationToken)
        );
    }

    /// <summary>
    /// Checks that publishing with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var topic = CreateKey("topic");
        var payload = "a"u8.ToArray();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Notifications.PublishAsync(
                topic,
                payload,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
