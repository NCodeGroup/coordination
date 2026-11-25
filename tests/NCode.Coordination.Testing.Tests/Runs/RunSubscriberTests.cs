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

    private static async ValueTask WriteAsync(Channel<int> channel, params int[] items)
    {
        foreach (var item in items)
        {
            await channel.Writer.WriteAsync(item, Token);
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
        await WriteAsync(channel, 1, 2);

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
        await WriteAsync(channel, 1, 2, 3);

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 3, Token);

        Assert.True(found);
        Assert.Equal<int>([1, 2, 3], subscriber.Deliveries.Select(delivery => delivery.Item));
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
        await WriteAsync(channel, 1, 2);
        Assert.True(await subscriber.WaitForAsync(delivery => delivery.Item == 1, Token));

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 1, Token);

        Assert.True(found);
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
        await WriteAsync(channel, 1);
        channel.Writer.Complete();

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 9, Token);

        Assert.False(found);
        Assert.Equal(1, Assert.Single(subscriber.Deliveries).Item);
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
        await WriteAsync(channel, 1, 2, 3);
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
        await WriteAsync(channel, 1);
        channel.Writer.Complete();

        var found = await subscriber.WaitForAsync(delivery => delivery.Item == 9, Token);

        Assert.False(found);
        Assert.Equal(1, Assert.Single(subscriber.Deliveries).Item);
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
        Assert.False(await subscriber.WaitForAsync(_ => false, Token));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await subscriber.DisposeAsync()
        );

        Assert.Equal("broken", exception.Message);
        Assert.True(subscriber.Stop.IsCancellationRequested);
    }

    #endregion
}
