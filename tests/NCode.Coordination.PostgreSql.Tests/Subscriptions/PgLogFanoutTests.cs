// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Subscriptions.Tests;

public sealed class PgLogFanoutTests
{
    private sealed record TestEntry(long Number) : IPgLogEntry;

    private sealed class FakeLog(long removed, long current, params long[] numbers)
    {
        public int Reads { get; private set; }

        public List<long> Afters { get; } = [];

        public ValueTask<PgLogRead<TestEntry>> ReadAsync(
            long after,
            int limit,
            CancellationToken cancellationToken
        )
        {
            Reads++;
            Afters.Add(after);

            var entries = numbers
                .Where(x => x > after)
                .OrderBy(x => x)
                .Take(limit)
                .Select(x => new TestEntry(x))
                .ToImmutableArray();

            return ValueTask.FromResult(new PgLogRead<TestEntry>(removed, current, entries));
        }
    }

    private static List<PgLogEvent<TestEntry>> Drain(PgLogSubscriber<TestEntry> subscriber)
    {
        var events = new List<PgLogEvent<TestEntry>>();
        while (subscriber.Reader.TryRead(out var item))
        {
            events.Add(item);
        }

        return events;
    }

    private static PgLogEvent<TestEntry> Delivered(long number) =>
        PgLogEvent<TestEntry>.ForEntry(new TestEntry(number), number);

    #region PumpAsync Tests

    [Fact]
    public async Task PumpAsync_NewSubscriber_ResyncsAtTheCurrentNumberFirst()
    {
        var log = new FakeLog(1, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddWithResync();

        await fanout.PumpAsync(CancellationToken.None);

        var resync = PgLogEvent<TestEntry>.ForResync(3);
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>([resync], events);
        Assert.Equal(3, subscriber.Cursor);
        Assert.False(subscriber.NeedsResync);
        Assert.Equal([long.MaxValue], log.Afters);
    }

    [Fact]
    public async Task PumpAsync_EntriesAfterTheCursor_DeliversThemInOrder()
    {
        var log = new FakeLog(1, 5, 1, 2, 3, 4, 5);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddAfter(2);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected = [Delivered(3), Delivered(4), Delivered(5)];
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, events);
        Assert.Equal(5, subscriber.Cursor);
        Assert.Equal([2L], log.Afters);
    }

    [Fact]
    public async Task PumpAsync_GapInTheEntries_Resyncs()
    {
        var log = new FakeLog(0, 4, 1, 2, 4);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddAfter(0);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected =
        [
            Delivered(1),
            Delivered(2),
            PgLogEvent<TestEntry>.ForResync(4),
        ];
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, events);
        Assert.Equal(4, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_CursorBelowRemoved_Resyncs()
    {
        var log = new FakeLog(5, 8, 6, 7, 8);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddAfter(2);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected = [PgLogEvent<TestEntry>.ForResync(8)];
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, events);
        Assert.Equal(8, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_CurrentBelowTheCursor_Resyncs()
    {
        var log = new FakeLog(0, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddAfter(7);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected = [PgLogEvent<TestEntry>.ForResync(3)];
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, events);
        Assert.Equal(3, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_TwoSubscribers_ReadsOnce()
    {
        var log = new FakeLog(1, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var behind = fanout.AddAfter(1);
        var ahead = fanout.AddAfter(2);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expectedBehind = [Delivered(2), Delivered(3)];
        PgLogEvent<TestEntry>[] expectedAhead = [Delivered(3)];
        var behindEvents = Drain(behind);
        var aheadEvents = Drain(ahead);

        Assert.Equal(1, log.Reads);
        Assert.Equal([1L], log.Afters);
        Assert.Equal<PgLogEvent<TestEntry>>(expectedBehind, behindEvents);
        Assert.Equal<PgLogEvent<TestEntry>>(expectedAhead, aheadEvents);
    }

    [Fact]
    public async Task PumpAsync_FullChannel_ResyncsAtTheNextPump()
    {
        var numbers = Enumerable.Range(1, 1_100).Select(x => (long)x).ToArray();
        var log = new FakeLog(0, 1_100, numbers);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.AddAfter(0);

        await fanout.PumpAsync(CancellationToken.None);

        var first = Drain(subscriber);
        Assert.Equal(PgLogSubscriber<TestEntry>.Capacity, first.Count);
        Assert.All(first, x => Assert.False(x.IsResync));
        Assert.True(subscriber.NeedsResync);

        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected = [PgLogEvent<TestEntry>.ForResync(1_100)];
        var events = Drain(subscriber);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, events);
        Assert.False(subscriber.NeedsResync);
        Assert.Equal(1_100, subscriber.Cursor);
    }

    #endregion

    #region ResyncAll Tests

    [Fact]
    public async Task ResyncAll_Always_ResyncsEach()
    {
        var log = new FakeLog(1, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var first = fanout.AddAfter(3);
        var second = fanout.AddAfter(3);

        await fanout.PumpAsync(CancellationToken.None);

        var firstEvents = Drain(first);
        var secondEvents = Drain(second);

        Assert.Empty(firstEvents);
        Assert.Empty(secondEvents);

        fanout.ResyncAll();
        await fanout.PumpAsync(CancellationToken.None);

        PgLogEvent<TestEntry>[] expected = [PgLogEvent<TestEntry>.ForResync(3)];
        var firstResync = Drain(first);
        var secondResync = Drain(second);

        Assert.Equal<PgLogEvent<TestEntry>>(expected, firstResync);
        Assert.Equal<PgLogEvent<TestEntry>>(expected, secondResync);
    }

    #endregion

    #region TryMarkWake Tests

    [Fact]
    public void TryMarkWake_Twice_ReturnsTrueThenFalse()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);

        var first = fanout.TryMarkWake();
        var second = fanout.TryMarkWake();

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryMarkWake_AfterAPumpBeganItsRead_ReturnsTrueAgain()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);
        fanout.AddAfter(0);

        var first = fanout.TryMarkWake();

        Assert.True(first);

        await fanout.PumpAsync(CancellationToken.None);

        var second = fanout.TryMarkWake();

        Assert.True(second);
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_LastSubscriber_ReturnsTrue()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);
        var first = fanout.AddAfter(0);
        var second = fanout.AddAfter(0);

        var removedFirst = fanout.Remove(first);
        var countAfterFirst = fanout.Count;
        var removedSecond = fanout.Remove(second);
        var countAfterSecond = fanout.Count;

        Assert.False(removedFirst);
        Assert.Equal(1, countAfterFirst);
        Assert.True(removedSecond);
        Assert.Equal(0, countAfterSecond);
    }

    #endregion
}
