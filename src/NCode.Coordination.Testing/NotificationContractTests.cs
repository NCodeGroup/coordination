// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
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
        await using var stream = ContractStreams.Open(subscription, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);

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
        await using var stream = ContractStreams.Open(subscription, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var payload = "a"u8.ToArray();

        await Notifications.PublishAsync(topic, payload, null, TestCancellationToken);

        var message = await ContractStreams.NextAsync(stream, TestCancellationToken);
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
        await using var stream = ContractStreams.Open(subscription, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var payloadA = "a"u8.ToArray();
        var payloadB = "b"u8.ToArray();

        await Notifications.PublishAsync(topic, payloadA, null, TestCancellationToken);
        await Notifications.PublishAsync(topic, payloadB, null, TestCancellationToken);

        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);
        var second = await ContractStreams.NextAsync(stream, TestCancellationToken);
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
        await using var first = ContractStreams.Open(firstSubscription, TestCancellationToken);
        var secondSubscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var second = ContractStreams.Open(secondSubscription, TestCancellationToken);
        await ContractStreams.NextAsync(first, TestCancellationToken);
        await ContractStreams.NextAsync(second, TestCancellationToken);

        var payload = "a"u8.ToArray();

        await Notifications.PublishAsync(topic, payload, null, TestCancellationToken);

        var firstMessage = await ContractStreams.NextAsync(first, TestCancellationToken);
        var secondMessage = await ContractStreams.NextAsync(second, TestCancellationToken);
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
        await using var stream = ContractStreams.Open(subscription, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var payloadA = "a"u8.ToArray();
        var payloadB = "b"u8.ToArray();

        await Notifications.PublishAsync(topicB, payloadB, null, TestCancellationToken);
        await Notifications.PublishAsync(topicA, payloadA, null, TestCancellationToken);

        var message = await ContractStreams.NextAsync(stream, TestCancellationToken);
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

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await Notifications.PublishAsync(topic, payload, null, TestCancellationToken)
        );

        Assert.Equal("payload", exception.ParamName);
    }

    /// <summary>
    /// Checks that publishing a payload of exactly the limit delivers a message of that length.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenPayloadIsAtTheLimit_Publishes()
    {
        var topic = CreateKey("topic");
        var subscription = Notifications.SubscribeAsync(topic, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(subscription, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var payload = new byte[CoordinationLimits.MaxPayloadLength];

        await Notifications.PublishAsync(topic, payload, null, TestCancellationToken);

        var message = await ContractStreams.NextAsync(stream, TestCancellationToken);
        Assert.False(message.IsResync);
        Assert.Equal(CoordinationLimits.MaxPayloadLength, message.Payload.Length);
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

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that two subscribers receive the messages that clients publish at once as the provider declares.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsPublish_DeliversAsDeclared() =>
        await RunDeliveryAsync(null, TestCancellationToken);

    /// <summary>
    /// Checks that two subscribers receive the messages as the provider declares when its connections drop in the
    /// middle of the run.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenConnectionsDrop_DeliversAsDeclared()
    {
        var fault = GetDropConnections();
        await RunDeliveryAsync(fault, TestCancellationToken);
    }

    /// <summary>
    /// Runs clients that publish to a topic that two subscribers read, with a fault, and asserts that what the
    /// subscribers received is as the provider declares.
    /// </summary>
    /// <param name="fault">The fault to inject once half of the steps are done, or null for none.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when the deliveries have been checked.</returns>
    private async ValueTask RunDeliveryAsync(
        Func<CancellationToken, ValueTask>? fault,
        CancellationToken cancellationToken
    )
    {
        var keepsDelivery = Keeps(Primitives.Notification, Guarantees.AtMostOnce);
        Assert.SkipUnless(
            keepsDelivery,
            "The provider declares no delivery guarantee for notifications."
        );

        var gapDetection = Keeps(Primitives.Notification, Guarantees.AtMostOnceWithGapDetection);
        var notifications = Notifications;
        var topic = CreateKey("run");
        var partition = topic.ToString();
        var recorder = new HistoryRecorder();

        await using var first = new RunSubscriber<Notification>(
            token => notifications.SubscribeAsync(topic, null, token),
            recorder,
            cancellationToken
        );
        await using var second = new RunSubscriber<Notification>(
            token => notifications.SubscribeAsync(topic, null, token),
            recorder,
            cancellationToken
        );
        var firstReady = await first.WaitForAsync(_ => true, cancellationToken);
        Assert.True(firstReady);
        var secondReady = await second.WaitForAsync(_ => true, cancellationToken);
        Assert.True(secondReady);

        await ConcurrentRun.RunAsync(
            ConcurrentRun.Steps,
            async (client, index, stepToken) =>
            {
                var payload = string.Create(CultureInfo.InvariantCulture, $"{client}-{index}");
                var bytes = HistoryValues.Encode(payload);
                await recorder.RecordAsync(
                    client,
                    partition,
                    new NotificationCall(payload),
                    token => notifications.PublishAsync(topic, bytes, null, token),
                    stepToken
                );
            },
            fault,
            cancellationToken
        );

        var lastBytes = HistoryValues.Encode(NotificationCall.Last);
        var last = await recorder.RecordAsync(
            HistoryOperation.TestClient,
            partition,
            new NotificationCall(NotificationCall.Last),
            token => notifications.PublishAsync(topic, lastBytes, null, token),
            cancellationToken
        );
        Assert.Equal(HistoryOutcome.Returned, last.Outcome);

        bool IsLastOrResync(Delivery<Notification> delivery)
        {
            if (delivery.Item.IsResync)
            {
                return gapDetection && delivery.Stamp > last.Responded;
            }

            var payload = HistoryValues.Decode(delivery.Item.Payload);
            return string.Equals(payload, NotificationCall.Last, StringComparison.Ordinal);
        }

        var firstDone = await first.WaitForAsync(IsLastOrResync, cancellationToken);
        var secondDone = await second.WaitForAsync(IsLastOrResync, cancellationToken);
        Assert.True(
            (firstDone && secondDone) || !gapDetection,
            "A subscriber neither received the last message nor a resync after it."
        );

        var history = recorder.ToHistory();
        var violations = NotificationDeliveryChecker.Check(
            history,
            [first.Deliveries, second.Deliveries],
            gapDetection
        );
        Assert.Empty(violations);
    }

    #endregion
}
