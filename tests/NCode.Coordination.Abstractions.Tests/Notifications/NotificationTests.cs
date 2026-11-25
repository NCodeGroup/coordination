// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class NotificationTests
{
    private static CoordinationKey Topic() => new(new Keyspace("prod"), "changes");

    #region Message Tests

    [Fact]
    public void Message_WhenArgumentsAreGiven_KeepsValues()
    {
        var topic = Topic();
        byte[] payload = [1, 2, 3];

        var notification = Notification.Message(topic, 7, payload);

        Assert.Equal(NotificationKind.Message, notification.Kind);
        Assert.Same(topic, notification.Topic);
        Assert.Equal(7, notification.Sequence);
        var kept = notification.Payload.ToArray();

        Assert.Equal<byte>(payload, kept);
        Assert.False(notification.IsResync);
    }

    [Fact]
    public void Message_WhenPayloadHasTheMaximumLength_Accepts()
    {
        var topic = Topic();

        var payload = new byte[CoordinationLimits.MaxPayloadLength];

        var notification = Notification.Message(topic, 1, payload);

        Assert.Equal(CoordinationLimits.MaxPayloadLength, notification.Payload.Length);
    }

    [Fact]
    public void Message_WhenPayloadIsTooLong_Throws()
    {
        var topic = Topic();

        var payload = new byte[CoordinationLimits.MaxPayloadLength + 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            Notification.Message(topic, 1, payload)
        );

        Assert.Equal("payload", exception.ParamName);
    }

    [Fact]
    public void Message_WhenSourceChangesAfterward_KeepsItsCopy()
    {
        const long sequence = 7;
        var topic = Topic();
        byte[] source = [1, 2, 3];

        var notification = Notification.Message(topic, sequence, source);

        source[0] = 9;
        var kept = notification.Payload.ToArray();

        Assert.Equal<byte>([1, 2, 3], kept);
    }

    [Fact]
    public void Message_WhenSequenceIsZero_Throws()
    {
        var topic = Topic();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Notification.Message(topic, 0, ReadOnlyMemory<byte>.Empty)
        );

        Assert.Equal("sequence", exception.ParamName);
    }

    [Fact]
    public void Message_WhenTopicIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            Notification.Message(null!, 1, ReadOnlyMemory<byte>.Empty)
        );

        Assert.Equal("topic", exception.ParamName);
    }

    #endregion

    #region Resync Tests

    [Fact]
    public void Resync_WhenArgumentsAreGiven_KeepsValuesWithEmptyPayload()
    {
        var topic = Topic();

        var notification = Notification.Resync(topic, 5);

        Assert.Equal(NotificationKind.Resync, notification.Kind);
        Assert.Same(topic, notification.Topic);
        Assert.Equal(5, notification.Sequence);
        Assert.True(notification.Payload.IsEmpty);
        Assert.True(notification.IsResync);
    }

    [Fact]
    public void Resync_WhenSequenceIsZero_Accepts()
    {
        var topic = Topic();

        var notification = Notification.Resync(topic, 0);

        Assert.Equal(0, notification.Sequence);
    }

    [Fact]
    public void Resync_WhenSequenceIsNegative_Throws()
    {
        var topic = Topic();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Notification.Resync(topic, -1)
        );

        Assert.Equal("sequence", exception.ParamName);
    }

    [Fact]
    public void Resync_WhenTopicIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Notification.Resync(null!, 0));

        Assert.Equal("topic", exception.ParamName);
    }

    #endregion
}
