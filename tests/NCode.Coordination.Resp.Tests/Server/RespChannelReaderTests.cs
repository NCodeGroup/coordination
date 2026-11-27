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

    private static async Task WriteAsync(Channel<RespSignal> signals, string text)
    {
        var signal = Message(text);
        await signals.Writer.WriteAsync(signal, TestContext.Current.CancellationToken);
    }

    private static (
        RespChannelReader Reader,
        Channel<RespSignal> Signals,
        FakeTimeProvider Time
    ) Create(params IEnumerable<Func<ValueTask<long>>> reads)
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

    private static Func<ValueTask<long>> Fails()
    {
        var failure = Unavailable();
        return () => ValueTask.FromException<long>(failure);
    }

    private static async Task<RespChannelItem> NextAsync(IAsyncEnumerator<RespChannelItem> items)
    {
        var moving = items.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(Timeout, TestContext.Current.CancellationToken);

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
        var seven = Number(7);
        var (reader, _, _) = Create(seven);
        await using var items = Open(reader);

        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    [Fact]
    public async Task ReadAsync_WhenTheFirstReadFails_Throws()
    {
        var failure = Fails();
        var (reader, _, _) = Create(failure);
        await using var items = Open(reader);

        var exception = await Assert.ThrowsAsync<CoordinationException>(() =>
            items.MoveNextAsync().AsTask()
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public async Task ReadAsync_WhenTheNextNumberArrives_YieldsTheMessage()
    {
        var seven = Number(7);
        var (reader, signals, _) = Create(seven);
        await using var items = Open(reader);
        await NextAsync(items);

        await WriteAsync(signals, "8\npayload");
        var item = await NextAsync(items);

        AssertMessage(item, 8);
        Assert.Equal("8\npayload", (string?)item.Message);
    }

    [Fact]
    public async Task ReadAsync_WhenMessagesAreAtOrBelowTheStart_DropsThem()
    {
        var seven = Number(7);
        var (reader, signals, _) = Create(seven);
        await using var items = Open(reader);
        await NextAsync(items);

        await WriteAsync(signals, "6");
        await WriteAsync(signals, "7");
        await WriteAsync(signals, "8");
        var item = await NextAsync(items);

        AssertMessage(item, 8);
    }

    [Fact]
    public async Task ReadAsync_WhenANumberIsSkipped_ResyncsAtTheChannelsNumber()
    {
        var seven = Number(7);
        var nine = Number(9);
        var (reader, signals, _) = Create(seven, nine);
        await using var items = Open(reader);
        await NextAsync(items);

        await WriteAsync(signals, "9");
        var item = await NextAsync(items);

        AssertResync(item, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenAMessageHasNoNumber_Resyncs()
    {
        var seven = Number(7);
        var eight = Number(8);
        var (reader, signals, _) = Create(seven, eight);
        await using var items = Open(reader);
        await NextAsync(items);

        await WriteAsync(signals, "x\npayload");
        var item = await NextAsync(items);

        AssertResync(item, 8);
    }

    [Fact]
    public async Task ReadAsync_WhenTheNumberGoesBackwardsAfterAMessage_Resyncs()
    {
        var seven = Number(7);
        var three = Number(3);
        var (reader, signals, _) = Create(seven, three);
        await using var items = Open(reader);
        await NextAsync(items);
        await WriteAsync(signals, "8");
        await NextAsync(items);

        await WriteAsync(signals, "3");
        var item = await NextAsync(items);

        AssertResync(item, 3);
    }

    [Fact]
    public async Task ReadAsync_WhenAConnectionIsRestored_Resyncs()
    {
        var seven = Number(7);
        var eight = Number(8);
        var (reader, signals, _) = Create(seven, eight);
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Restored, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 8);
    }

    [Fact]
    public async Task ReadAsync_WhenACheckFindsTheChannelAheadPastTheGrace_Resyncs()
    {
        var seven = Number(7);
        var nine = Number(9);
        var (reader, signals, time) = Create(seven, nine, nine);
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var moving = items.MoveNextAsync().AsTask();
        time.Advance(RespChannelReader.Grace);
        var item = await FinishAsync(moving, items);

        AssertResync(item, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenACheckFindsTheChannelBehind_ResyncsAtTheChannelsNumber()
    {
        var seven = Number(7);
        var five = Number(5);
        var (reader, signals, _) = Create(seven, five, five);
        await using var items = Open(reader);
        await NextAsync(items);
        await WriteAsync(signals, "8");
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 5);
    }

    [Fact]
    public async Task ReadAsync_WhenChecksArriveWithinTheGrace_ResyncsOnce()
    {
        var seven = Number(7);
        var nine = Number(9);
        var (reader, signals, time) = Create(seven, nine, nine);
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
        var seven = Number(7);
        var eight = Number(8);
        var (reader, signals, time) = Create(seven, eight);
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var moving = items.MoveNextAsync().AsTask();
        await WriteAsync(signals, "8");
        time.Advance(RespChannelReader.Grace);
        var first = await FinishAsync(moving, items);
        await WriteAsync(signals, "9");
        var second = await NextAsync(items);

        AssertMessage(first, 8);
        AssertMessage(second, 9);
    }

    [Fact]
    public async Task ReadAsync_WhenAConnectionFailed_ResyncsAtTheNextCheck()
    {
        var seven = Number(7);
        var (reader, signals, _) = Create(seven, seven, seven);
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
        var seven = Number(7);
        var (reader, signals, _) = Create(seven, seven, seven);
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
        var seven = Number(7);
        var failure = Fails();
        var (reader, signals, _) = Create(seven, failure, seven, seven);
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 7);
    }

    [Fact]
    public async Task ReadAsync_WhenAResyncsReadFails_ResyncsAtTheNextCheck()
    {
        var seven = Number(7);
        var failure = Fails();
        var nine = Number(9);
        var (reader, signals, _) = Create(seven, failure, nine, nine);
        await using var items = Open(reader);
        await NextAsync(items);

        await signals.Writer.WriteAsync(RespSignal.Restored, TestContext.Current.CancellationToken);
        await signals.Writer.WriteAsync(RespSignal.Check, TestContext.Current.CancellationToken);
        var item = await NextAsync(items);

        AssertResync(item, 9);
    }

    #endregion
}
