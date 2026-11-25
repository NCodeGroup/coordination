// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Barriers.Tests;

public sealed class DefaultBarrierServiceTests
{
    private static CoordinationKey CreateBarrier() => RecipeTesting.Keyspace.CreateKey("barrier");

    private static DefaultBarrierService CreateBarriers(IServiceProvider services) =>
        new(
            services.GetRequiredService<IRecordService>(),
            services.GetRequiredService<IRecordWatchService>()
        );

    #region ArriveAndWaitAsync Tests

    [Fact]
    public async Task ArriveAndWaitAsync_WhenAPartyIsMissing_WaitsUntilItArrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(3, RecordLifetime.Persistent);
        var first = barriers
            .ArriveAndWaitAsync(barrier, KeySegment.FromString("a"), terms, null, cancellationToken)
            .AsTask();
        var second = barriers
            .ArriveAndWaitAsync(barrier, KeySegment.FromString("b"), terms, null, cancellationToken)
            .AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        var third = await barriers.ArriveAndWaitAsync(
            barrier,
            KeySegment.FromString("c"),
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
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(8, RecordLifetime.Persistent);

        var results = await RecipeTesting.RunConcurrentlyAsync(
            8,
            index =>
                barriers.ArriveAndWaitAsync(
                    barrier,
                    KeySegment.FromInt64(index),
                    terms,
                    null,
                    cancellationToken
                ),
            cancellationToken
        );

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task ArriveAndWaitAsync_WhenAParticipantArrivesTwice_CountsItOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var terms = new BarrierTerms(2, RecordLifetime.Persistent);
        var first = barriers
            .ArriveAndWaitAsync(barrier, KeySegment.FromString("a"), terms, null, cancellationToken)
            .AsTask();
        var again = barriers
            .ArriveAndWaitAsync(barrier, KeySegment.FromString("a"), terms, null, cancellationToken)
            .AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(again.IsCompleted);

        var other = await barriers.ArriveAndWaitAsync(
            barrier,
            KeySegment.FromString("b"),
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
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var sessions = services.GetRequiredService<ISessionService>();
        var barriers = CreateBarriers(services);
        var barrier = CreateBarrier();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);
        var terms = new BarrierTerms(1, RecordLifetime.ForSession(session));

        var passed = await barriers.ArriveAndWaitAsync(
            barrier,
            KeySegment.FromString("a"),
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
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var barriers = CreateBarriers(services);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = KeySegment.FromInt64(i);
        }

        var barrier = new CoordinationKey(RecipeTesting.Keyspace, segments);
        var terms = new BarrierTerms(1, RecordLifetime.Persistent);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await barriers.ArriveAndWaitAsync(
                barrier,
                KeySegment.FromString("a"),
                terms,
                null,
                cancellationToken
            )
        );

        Assert.Equal("barrier", exception.ParamName);
    }

    #endregion
}
