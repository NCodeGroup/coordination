// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Runs.Tests;

public sealed class RunSubscriberTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async IAsyncEnumerable<int> StreamAsync(
        Channel<int> channel,
        Exception? ending,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        if (ending is not null)
        {
            throw ending;
        }
    }

    private static async ValueTask WriteAsync(
        Channel<int> channel,
        IReadOnlyList<int> items,
        CancellationToken cancellationToken
    )
    {
        foreach (var item in items)
        {
            await channel.Writer.WriteAsync(item, cancellationToken);
        }
    }

    #region WaitForAsync Tests

    [Fact]
    public async ValueTask WaitForAsync_WhenFirstItemMatches_ReturnsTrue()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1, 2], Token);

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 1, Token);

        Assert.True(found);
        Assert.Equal(1, subscriber.Received[0].Item);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenLaterItemMatches_ReturnsTrueAfterReadingEarlierItems()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1, 2, 3], Token);

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 3, Token);

        Assert.True(found);
        var items = subscriber.Deliveries.Select(delivery => delivery.Item);
        Assert.Equal<int>([1, 2, 3], items);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenItemAlreadyReceived_ReturnsTrueWithoutReadingMore()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1, 2], Token);
        var receivedFirst = await subscriber.WaitForAsync(delivery => delivery.Item == 1, Token);
        Assert.True(receivedFirst);

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 1, Token);

        Assert.True(found);
        var received = Assert.Single(subscriber.Deliveries);
        Assert.Equal(1, received.Item);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenSubscriptionEndsWithoutMatch_ReturnsFalse()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1], Token);
        channel.Writer.Complete();

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 9, Token);

        Assert.False(found);
        var received = Assert.Single(subscriber.Deliveries);
        Assert.Equal(1, received.Item);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenNothingArrivesWithinTheTimeout_ReturnsFalse()
    {
        var channel = Channel.CreateUnbounded<int>();
        var timeout = TimeSpan.FromMilliseconds(50);
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            timeout,
            Token
        );

        var found = await subscriber.WaitForAsync(_ => true, Token);

        Assert.False(found);
        Assert.Empty(subscriber.Deliveries);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenItemsArrive_StampsIncrease()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, null, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1, 2, 3], Token);
        channel.Writer.Complete();

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 9, Token);

        Assert.False(found);
        var stamps = subscriber.Deliveries.Select(delivery => delivery.Stamp).ToArray();
        Assert.Equal(3, stamps.Length);
        Assert.True(stamps[0] < stamps[1]);
        Assert.True(stamps[1] < stamps[2]);
    }

    [Fact]
    public async ValueTask WaitForAsync_WhenSubscriptionFailsWithCoordinationException_ReturnsFalse()
    {
        var channel = Channel.CreateUnbounded<int>();
        var failure = new CoordinationException(CoordinationErrorCodes.Unavailable, true, "down");
        await using var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, failure, token),
            new HistoryRecorder(),
            Token
        );
        await WriteAsync(channel, [1], Token);
        channel.Writer.Complete();

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 9, Token);

        Assert.False(found);
        var received = Assert.Single(subscriber.Deliveries);
        Assert.Equal(1, received.Item);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async ValueTask DisposeAsync_WhenSubscriptionFailsWithOtherException_Rethrows()
    {
        var channel = Channel.CreateUnbounded<int>();
        var subscriber = new RunSubscriber<int>(
            token => StreamAsync(channel, new InvalidOperationException("broken"), token),
            new HistoryRecorder(),
            Token
        );
        channel.Writer.Complete();
        var found = await subscriber.WaitForAsync(_ => false, Token);
        Assert.False(found);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await subscriber.DisposeAsync()
        );

        Assert.Equal("broken", exception.Message);
        Assert.True(subscriber.Stop.IsCancellationRequested);
    }

    #endregion
}
