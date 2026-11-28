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
        var subscriber = fanout.Add(0, resyncFirst: true);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [PgLogEvent<TestEntry>.ForResync(3)],
            Drain(subscriber)
        );
        Assert.Equal(3, subscriber.Cursor);
        Assert.False(subscriber.NeedsResync);
        Assert.Equal([long.MaxValue], log.Afters);
    }

    [Fact]
    public async Task PumpAsync_EntriesAfterTheCursor_DeliversThemInOrder()
    {
        var log = new FakeLog(1, 5, 1, 2, 3, 4, 5);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.Add(2, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [Delivered(3), Delivered(4), Delivered(5)],
            Drain(subscriber)
        );
        Assert.Equal(5, subscriber.Cursor);
        Assert.Equal([2L], log.Afters);
    }

    [Fact]
    public async Task PumpAsync_GapInTheEntries_Resyncs()
    {
        var log = new FakeLog(0, 4, 1, 2, 4);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.Add(0, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [Delivered(1), Delivered(2), PgLogEvent<TestEntry>.ForResync(4)],
            Drain(subscriber)
        );
        Assert.Equal(4, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_CursorBelowRemoved_Resyncs()
    {
        var log = new FakeLog(5, 8, 6, 7, 8);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.Add(2, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [PgLogEvent<TestEntry>.ForResync(8)],
            Drain(subscriber)
        );
        Assert.Equal(8, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_CurrentBelowTheCursor_Resyncs()
    {
        var log = new FakeLog(0, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.Add(7, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [PgLogEvent<TestEntry>.ForResync(3)],
            Drain(subscriber)
        );
        Assert.Equal(3, subscriber.Cursor);
    }

    [Fact]
    public async Task PumpAsync_TwoSubscribers_ReadsOnce()
    {
        var log = new FakeLog(1, 3, 1, 2, 3);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var behind = fanout.Add(1, resyncFirst: false);
        var ahead = fanout.Add(2, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal(1, log.Reads);
        Assert.Equal([1L], log.Afters);
        Assert.Equal<PgLogEvent<TestEntry>>([Delivered(2), Delivered(3)], Drain(behind));
        Assert.Equal<PgLogEvent<TestEntry>>([Delivered(3)], Drain(ahead));
    }

    [Fact]
    public async Task PumpAsync_FullChannel_ResyncsAtTheNextPump()
    {
        var numbers = Enumerable.Range(1, 1_100).Select(x => (long)x).ToArray();
        var log = new FakeLog(0, 1_100, numbers);
        var fanout = new PgLogFanout<TestEntry>(log.ReadAsync);
        var subscriber = fanout.Add(0, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        var first = Drain(subscriber);
        Assert.Equal(PgLogSubscriber<TestEntry>.Capacity, first.Count);
        Assert.All(first, x => Assert.False(x.IsResync));
        Assert.True(subscriber.NeedsResync);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>(
            [PgLogEvent<TestEntry>.ForResync(1_100)],
            Drain(subscriber)
        );
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
        var first = fanout.Add(3, resyncFirst: false);
        var second = fanout.Add(3, resyncFirst: false);

        await fanout.PumpAsync(CancellationToken.None);

        Assert.Empty(Drain(first));
        Assert.Empty(Drain(second));

        fanout.ResyncAll();
        await fanout.PumpAsync(CancellationToken.None);

        Assert.Equal<PgLogEvent<TestEntry>>([PgLogEvent<TestEntry>.ForResync(3)], Drain(first));
        Assert.Equal<PgLogEvent<TestEntry>>([PgLogEvent<TestEntry>.ForResync(3)], Drain(second));
    }

    #endregion

    #region TryMarkWake Tests

    [Fact]
    public void TryMarkWake_Twice_ReturnsTrueThenFalse()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);

        Assert.True(fanout.TryMarkWake());
        Assert.False(fanout.TryMarkWake());
    }

    [Fact]
    public async Task TryMarkWake_AfterAPumpBeganItsRead_ReturnsTrueAgain()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);
        fanout.Add(0, resyncFirst: false);

        Assert.True(fanout.TryMarkWake());

        await fanout.PumpAsync(CancellationToken.None);

        Assert.True(fanout.TryMarkWake());
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_LastSubscriber_ReturnsTrue()
    {
        var fanout = new PgLogFanout<TestEntry>(new FakeLog(0, 0).ReadAsync);
        var first = fanout.Add(0, resyncFirst: false);
        var second = fanout.Add(0, resyncFirst: false);

        Assert.False(fanout.Remove(first));
        Assert.Equal(1, fanout.Count);
        Assert.True(fanout.Remove(second));
        Assert.Equal(0, fanout.Count);
    }

    #endregion
}
