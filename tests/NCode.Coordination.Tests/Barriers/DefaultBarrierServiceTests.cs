// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Barriers.Tests;

public sealed class DefaultBarrierServiceTests
{
    private static CoordinationKey CreateBarrier() => RecipeTesting.Keyspace.CreateKey("barrier");

    private static DefaultBarrierService CreateBarriers(IServiceProvider services)
    {
        var records = services.GetRequiredService<IRecordService>();
        var watches = services.GetRequiredService<IRecordWatchService>();

        return new(records, watches);
    }

    #region ArriveAndWaitAsync Tests

    [Fact]
    public async Task ArriveAndWaitAsync_WhenAPartyIsMissing_WaitsUntilItArrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(3, RecordLifetime.Persistent);
        var partyA = KeySegment.FromString("a");
        var partyB = KeySegment.FromString("b");
        var partyC = KeySegment.FromString("c");
        var first = barriers
            .ArriveAndWaitAsync(barrier, partyA, terms, null, cancellationToken)
            .AsTask();
        var second = barriers
            .ArriveAndWaitAsync(barrier, partyB, terms, null, cancellationToken)
            .AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        var third = await barriers.ArriveAndWaitAsync(
            barrier,
            partyC,
            terms,
            null,
            cancellationToken
        );

        Assert.True(third);
        Assert.True(await first);
        Assert.True(await second);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenEveryPartyArrivesAtOnce_PassesThemAll()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(8, RecordLifetime.Persistent);

        var results = await RecipeTesting.RunConcurrentlyAsync(
            8,
            index =>
            {
                var party = KeySegment.FromInt64(index);
                return barriers.ArriveAndWaitAsync(barrier, party, terms, null, cancellationToken);
            },
            cancellationToken
        );

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenAParticipantArrivesTwice_CountsItOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(2, RecordLifetime.Persistent);
        var partyA = KeySegment.FromString("a");
        var partyB = KeySegment.FromString("b");
        var first = barriers
            .ArriveAndWaitAsync(barrier, partyA, terms, null, cancellationToken)
            .AsTask();
        var again = barriers
            .ArriveAndWaitAsync(barrier, partyA, terms, null, cancellationToken)
            .AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(again.IsCompleted);

        var other = await barriers.ArriveAndWaitAsync(
            barrier,
            partyB,
            terms,
            null,
            cancellationToken
        );

        Assert.True(other);
        Assert.True(await first);
        Assert.True(await again);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenTheSessionHasEnded_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var sessions = services.GetRequiredService<ISessionService>();
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);
        var lifetime = RecordLifetime.ForSession(session);
        var terms = new BarrierTerms(1, lifetime);
        var party = KeySegment.FromString("a");

        var passed = await barriers.ArriveAndWaitAsync(
            barrier,
            party,
            terms,
            null,
            cancellationToken
        );

        Assert.False(passed);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenTheBarrierHasTheMostSegments_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var barriers = CreateBarriers(services);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = KeySegment.FromInt64(i);
        }

        var barrier = new CoordinationKey(RecipeTesting.Keyspace, segments);
        var terms = new BarrierTerms(1, RecordLifetime.Persistent);
        var party = KeySegment.FromString("a");

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await barriers.ArriveAndWaitAsync(barrier, party, terms, null, cancellationToken)
        );

        Assert.Equal("barrier", exception.ParamName);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenTheWatchResyncs_RelistsTheArrivals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var records = services.GetRequiredService<IRecordService>();
        var barrier = CreateBarrier();
        var arrival = RecipeTesting.Keyspace.CreateKey("barrier", "b");
        var resyncing = new ResyncingWatches(records, arrival);
        var barriers = new DefaultBarrierService(records, resyncing);
        var terms = new BarrierTerms(2, RecordLifetime.Persistent);
        var party = KeySegment.FromString("a");

        var passed = await barriers.ArriveAndWaitAsync(
            barrier,
            party,
            terms,
            null,
            cancellationToken
        );

        Assert.True(passed);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenTheStreamEndsWithoutAFailure_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var records = services.GetRequiredService<IRecordService>();
        var ending = new EndingWatches();
        var barriers = new DefaultBarrierService(records, ending);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(2, RecordLifetime.Persistent);
        var party = KeySegment.FromString("a");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            barriers.ArriveAndWaitAsync(barrier, party, terms, null, cancellationToken).AsTask()
        );

        Assert.Equal("The watch ended without a failure.", exception.Message);
    }

    #endregion

    private sealed class EndingWatches : IRecordWatchService
    {
        public async IAsyncEnumerable<RecordChange> WatchKeyAsync(
            CoordinationKey key,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            yield break;
        }

        public async IAsyncEnumerable<RecordChange> WatchPrefixAsync(
            CoordinationKey prefix,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            yield break;
        }
    }

    private sealed class ResyncingWatches(IRecordService records, CoordinationKey arrival)
        : IRecordWatchService
    {
        public async IAsyncEnumerable<RecordChange> WatchKeyAsync(
            CoordinationKey key,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            yield break;
        }

        public async IAsyncEnumerable<RecordChange> WatchPrefixAsync(
            CoordinationKey prefix,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            var written = await records.CreateAsync(
                arrival,
                ReadOnlyMemory<byte>.Empty,
                RecordLifetime.Persistent,
                options,
                cancellationToken
            );
            var revision = Assert.IsType<Revision>(written.Revision);

            yield return RecordChange.Resync(revision);
        }
    }
}
