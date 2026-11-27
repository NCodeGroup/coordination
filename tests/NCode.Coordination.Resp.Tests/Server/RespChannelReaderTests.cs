// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespChannelReaderTests
{
    private static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(5);

    private static CoordinationException Unavailable() =>
        new(CoordinationErrorCodes.Unavailable, false, "The test's server is unavailable.");

    private static RespSignal Message(string text) => RespSignal.ForMessage((RedisValue)text);

    private static (
        RespChannelReader Reader,
        Channel<RespSignal> Signals,
        FakeTimeProvider Time
    ) Create(params Func<ValueTask<long>>[] reads)
    {
        var signals = Channel.CreateUnbounded<RespSignal>();
        var time = new FakeTimeProvider();
        var pending = new Queue<Func<ValueTask<long>>>(reads);
        var reader = new RespChannelReader(signals.Reader, _ => pending.Dequeue()(), time, null);
        return (reader, signals, time);
    }

    private static IAsyncEnumerator<RespChannelItem> Open(RespChannelReader reader)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return reader.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private static Func<ValueTask<long>> Number(long value) => () => ValueTask.FromResult(value);

    private static Func<ValueTask<long>> Fails() =>
        () => ValueTask.FromException<long>(Unavailable());

    private static async Task<RespChannelItem> NextAsync(IAsyncEnumerator<RespChannelItem> items)
    {
        var moved = await items
            .MoveNextAsync()
            .AsTask()
            .WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(moved);
        return items.Current;
    }

    private static async Task<RespChannelItem> FinishAsync(
        Task<bool> moving,
        IAsyncEnumerator<RespChannelItem> items
    )
    {
        var moved = await moving.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(moved);
        return items.Current;
    }

    private static void AssertResync(RespChannelItem item, long number)
    {
        Assert.True(item.IsResync);
        Assert.Equal(number, item.Number);
    }

    private static void AssertMessage(RespChannelItem item, long number)
    {
        Assert.False(item.IsResync);
        Assert.Equal(number, item.Number);
    }

    #region ParseNumber Tests

    [Theory]
    [InlineData("8", 8)]
    [InlineData("8\npayload", 8)]
    [InlineData("12\nw1:ta\nd1:tb", 12)]
    public void ParseNumber_WhenTheMessageStartsWithANumber_ReturnsIt(string text, long expected)
    {
        var number = RespChannelReader.ParseNumber((RedisValue)text);

        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0\npayload")]
    [InlineData("-1")]
    [InlineData("x\npayload")]
    [InlineData("\npayload")]
    public void ParseNumber_WhenTheMessageHasNoNumberOfOneOrMore_ReturnsNull(string text)
    {
        var number = RespChannelReader.ParseNumber((RedisValue)text);

        Assert.Null(number);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenStarted_YieldsAResyncAtTheChannelsNumber()
    {
        var (reader, _, _) = Create(Number(7));
        await using var items = Open(reader);

        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    [Fact]
    public async Task ReadAsync_WhenTheFirstReadFails_Throws()
    {
        var (reader, _, _) = Create(Fails());
        await using var items = Open(reader);

        var exception = await Assert.ThrowsAsync<CoordinationException>(() =>
            items.MoveNextAsync().AsTask()
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public async Task ReadAsync_WhenTheNextNumberArrives_YieldsTheMessage()
    {
        var (reader, signals, _) = Create(Number(7));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(
            Message("8\npayload"),
            TestContext.Current.CancellationToken
        );
        var item = await NextAsync(items);

        AssertMessage(item, 8);
        Assert.Equal("8\npayload", (string?)item.Message);
    }

    [Fact]
    public async Task ReadAsync_WhenMessagesAreAtOrBelowTheStart_DropsThem()
    {
        var (reader, signals, _) = Create(Number(7));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(Message("6"), TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(Message("7"), TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(Message("8"), TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertMessage(item, 8);
    }

    [Fact]
    public async Task ReadAsync_WhenANumberIsSkipped_ResyncsAtTheChannelsNumber()
    {
        var (reader, signals, _) = Create(Number(7), Number(9));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(Message("9"), TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenTheNumberGoesBackwardsAfterAMessage_Resyncs()
    {
        var (reader, signals, _) = Create(Number(7), Number(3));
        await using var items = Open(reader);
        await NextAsync(items);
        await signals.Writer.WriteAsync(Message("8"), TestContext.Current.CancellationToken);
        await NextAsync(items);

        await signals.Writer.WriteAsync(Message("3"), TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 3);
    }

    [Fact]
    public async Task ReadAsync_WhenAConnectionIsRestored_Resyncs()
    {
        var (reader, signals, _) = Create(Number(7), Number(8));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Restored, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 8);
    }

    [Fact]
    public async Task ReadAsync_WhenACheckFindsTheChannelAheadPastTheGrace_Resyncs()
    {
        var (reader, signals, time) = Create(Number(7), Number(9), Number(9));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var moving = items.MoveNextAsync().AsTask();
        time.Advance(RespChannelReader.Grace);
        var item = await FinishAsync(moving, items);

        AssertResync(item, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenChecksArriveWithinTheGrace_ResyncsOnce()
    {
        var (reader, signals, time) = Create(Number(7), Number(9), Number(9));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var moving = items.MoveNextAsync().AsTask();
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        time.Advance(RespChannelReader.Grace);
        var item = await FinishAsync(moving, items);

        AssertResync(item, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenTheMessagesArriveWithinTheGrace_DoesNotResync()
    {
        var (reader, signals, time) = Create(Number(7), Number(8));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var moving = items.MoveNextAsync().AsTask();
        await signals.Writer.WriteAsync(Message("8"), TestContext.Current.CancellationToken);
        time.Advance(RespChannelReader.Grace);
        var first = await FinishAsync(moving, items);
        await signals.Writer.WriteAsync(Message("9"), TestContext.Current.CancellationToken);
        var second = await NextAsync(items);

        AssertMessage(first, 8);
        AssertMessage(second, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenAConnectionFailed_ResyncsAtTheNextCheck()
    {
        var (reader, signals, _) = Create(Number(7), Number(7), Number(7));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Failed, TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    [Fact]
    public async Task ReadAsync_WhenMarkedInDoubt_ResyncsAtTheNextCheck()
    {
        var (reader, signals, _) = Create(Number(7), Number(7), Number(7));
        await using var items = Open(reader);
        await NextAsync(items);

        reader.MarkInDoubt();
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    [Fact]
    public async Task ReadAsync_WhenACheckFails_ResyncsAtTheNextCheck()
    {
        var (reader, signals, _) = Create(Number(7), Fails(), Number(7), Number(7));
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    #endregion
}
