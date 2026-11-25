// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class NotificationDeliveryCheckerTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Topic { get; } = new(Space, "topic");

    private static HistoryOperation Publish(string payload) =>
        HistoryTesting.Operation(0, "p", new NotificationCall(payload), 1, 2, null);

    private static Delivery<Notification> Message(long stamp, long sequence, string payload) =>
        new(stamp, Notification.Message(Topic, sequence, HistoryValues.Encode(payload)));

    private static Delivery<Notification> Resync(long stamp, long sequence) =>
        new(stamp, Notification.Resync(Topic, sequence));

    private static ImmutableArray<Delivery<Notification>> Subscriber(
        params Delivery<Notification>[] deliveries
    ) => [.. deliveries];

    #region Check Tests

    [Fact]
    public void Check_WhenDeliveriesAreClean_ReportsNothing()
    {
        HistoryOperation[] history = [Publish("a"), Publish("b")];
        var first = Subscriber(Resync(3, 0), Message(4, 1, "a"), Message(5, 2, "b"));
        var second = Subscriber(Resync(3, 0), Message(4, 1, "a"), Resync(5, 1), Message(6, 2, "b"));

        var violations = NotificationDeliveryChecker.Check(history, [first, second], true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenNoCallPublishedAMessage_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a")];
        var subscriber = Subscriber(Message(3, 1, "x"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Equal(["Subscriber 0 received 'x', which no call published before it."], violations);
    }

    [Fact]
    public void Check_WhenThePublishCameAfterTheDelivery_ReportsIt()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", new NotificationCall("a"), 5, 6, null),
        ];
        var subscriber = Subscriber(Message(3, 1, "a"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Equal(["Subscriber 0 received 'a', which no call published before it."], violations);
    }

    [Fact]
    public void Check_WhenThePublishFailed_ReportsIt()
    {
        HistoryOperation[] history =
        [
            new(0, "p", new NotificationCall("a"), 1, 2, HistoryOutcome.Failed, null),
        ];
        var subscriber = Subscriber(Message(3, 1, "a"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Equal(["Subscriber 0 received 'a', which no call published before it."], violations);
    }

    [Fact]
    public void Check_WhenThePublishIsIndeterminate_AcceptsTheMessage()
    {
        HistoryOperation[] history = [HistoryTesting.Indeterminate(new NotificationCall("a"))];
        var subscriber = Subscriber(Message(3, 1, "a"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenANumberDoesNotIncrease_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a"), Publish("b")];
        var subscriber = Subscriber(Message(3, 2, "a"), Message(4, 2, "b"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Contains("Subscriber 0 received message 2 after message 2.", violations);
    }

    [Fact]
    public void Check_WhenAGapHasNoResync_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a"), Publish("b")];
        var subscriber = Subscriber(Message(3, 1, "a"), Message(4, 3, "b"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], true);

        Assert.Equal(
            ["Subscriber 0 received message 3 after message 1 without a resync."],
            violations
        );
    }

    [Fact]
    public void Check_WhenAGapHasNoResyncAndGapDetectionIsNotDeclared_ReportsNothing()
    {
        HistoryOperation[] history = [Publish("a"), Publish("b")];
        var subscriber = Subscriber(Message(3, 1, "a"), Message(4, 3, "b"));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenAResyncCarriesTheWrongNumber_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a")];
        var subscriber = Subscriber(Message(3, 1, "a"), Resync(4, 2));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], true);

        Assert.Equal(["Subscriber 0 received a resync carrying 2 after message 1."], violations);
    }

    [Fact]
    public void Check_WhenAResyncCarriesTheWrongNumberAndGapDetectionIsNotDeclared_ReportsNothing()
    {
        HistoryOperation[] history = [Publish("a")];
        var subscriber = Subscriber(Message(3, 1, "a"), Resync(4, 2));

        var violations = NotificationDeliveryChecker.Check(history, [subscriber], false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenTwoSubscribersSeeDifferentPayloadsForANumber_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a"), Publish("b")];
        var first = Subscriber(Message(3, 1, "a"));
        var second = Subscriber(Message(4, 1, "b"));

        var violations = NotificationDeliveryChecker.Check(history, [first, second], true);

        Assert.Equal(["Message 1 carried 'a' and 'b'."], violations);
    }

    [Fact]
    public void Check_WhenTwoSubscribersSeeDifferentNumbersForAPayload_ReportsIt()
    {
        HistoryOperation[] history = [Publish("a")];
        var first = Subscriber(Message(3, 1, "a"));
        var second = Subscriber(Message(4, 2, "a"));

        var violations = NotificationDeliveryChecker.Check(history, [first, second], false);

        Assert.Equal(["'a' was numbered 1 and 2."], violations);
    }

    #endregion
}
