// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Singletons.Tests;

public sealed class DefaultSingletonServiceTests
{
    private static CoordinationKey CreateRole() => RecipeTesting.Keyspace.CreateKey("role");

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_WhenTwoCandidatesRun_RunsTheWorkOneAtATime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var first = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var second = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var firstCandidate = new SingletonCandidate(first);
        var secondCandidate = new SingletonCandidate(second);
        var events = new ConcurrentQueue<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            events.Enqueue("start");
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            events.Enqueue("end");
        }

        var firstTask = singletons
            .RunAsync(role, firstCandidate, Work, null, cancellationToken)
            .AsTask();
        var secondTask = singletons
            .RunAsync(role, secondCandidate, Work, null, cancellationToken)
            .AsTask();

        await started.Task.WaitAsync(cancellationToken);
        release.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);
        var recorded = events.ToArray();

        Assert.All(results, Assert.True);
        Assert.Equal(["start", "end", "start", "end"], recorded);
    }

    [Fact]
    public async Task RunAsync_WhenTheLeadershipIsLost_CancelsTheWorkAndCampaignsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var candidate = new SingletonCandidate(session);
        var calls = new int[1];
        var firstLeadership = new TaskCompletionSource<Leadership>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var firstToken = CancellationToken.None;

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls[0]) != 1)
            {
                return;
            }

            firstToken = token;
            firstLeadership.SetResult(leadership);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var task = singletons.RunAsync(role, candidate, Work, null, cancellationToken).AsTask();

        var leadership = await firstLeadership.Task.WaitAsync(cancellationToken);
        var resigned = await elections.ResignAsync(leadership, null, cancellationToken);
        var completed = await task;
        var totalCalls = Volatile.Read(ref calls[0]);

        Assert.True(resigned);
        Assert.True(completed);
        Assert.Equal(2, totalCalls);
        Assert.True(firstToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_WhenTheWatchFails_CancelsTheWorkAndCampaignsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var failure = new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            outcomeUnknown: false,
            "The provider is unreachable."
        );
        var failing = new StoppingWatchElections(elections, failure);
        var singletons = new DefaultSingletonService(failing);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var candidate = new SingletonCandidate(session);
        var calls = new int[1];
        var firstToken = CancellationToken.None;

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls[0]) != 1)
            {
                return;
            }

            firstToken = token;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var completed = await singletons.RunAsync(role, candidate, Work, null, cancellationToken);
        var totalCalls = Volatile.Read(ref calls[0]);

        Assert.True(completed);
        Assert.Equal(2, totalCalls);
        Assert.True(firstToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_WhenTheSessionHasEnded_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var sessions = services.GetRequiredService<ISessionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);
        var candidate = new SingletonCandidate(session);
        var calls = new int[1];

        var completed = await singletons.RunAsync(
            role,
            candidate,
            (_, _) =>
            {
                Interlocked.Increment(ref calls[0]);
                return ValueTask.CompletedTask;
            },
            null,
            cancellationToken
        );
        var totalCalls = Volatile.Read(ref calls[0]);

        Assert.False(completed);
        Assert.Equal(0, totalCalls);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileLeading_ResignsAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var candidate = new SingletonCandidate(session);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = singletons
            .RunAsync(
                role,
                candidate,
                async (_, token) =>
                {
                    started.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                null,
                cancellation.Token
            )
            .AsTask();

        await started.Task.WaitAsync(cancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var view = await elections.ReadAsync(role, null, cancellationToken);
        Assert.Null(view.Leader);
    }

    [Fact]
    public async Task RunAsync_WhenTheStreamEndsWithoutAFailure_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var ending = new StoppingWatchElections(elections, failure: null);
        var singletons = new DefaultSingletonService(ending);
        var role = CreateRole();
        var leaderSession = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var followerSession = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var leaderTerms = LeaseTerms.ForSession(leaderSession);
        var leading = await elections.TryLeadAsync(
            role,
            ReadOnlyMemory<byte>.Empty,
            leaderTerms,
            null,
            cancellationToken
        );
        Assert.True(leading.IsElected);
        var candidate = new SingletonCandidate(followerSession);
        var calls = new int[1];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            singletons
                .RunAsync(
                    role,
                    candidate,
                    (_, _) =>
                    {
                        Interlocked.Increment(ref calls[0]);
                        return ValueTask.CompletedTask;
                    },
                    null,
                    cancellationToken
                )
                .AsTask()
        );
        var totalCalls = Volatile.Read(ref calls[0]);

        Assert.Equal("The watch ended without a failure.", exception.Message);
        Assert.Equal(0, totalCalls);
    }

    [Fact]
    public async Task RunAsync_WhenTheLeadersWatchEndsWithoutAFailure_ResignsAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var elections = services.GetRequiredService<IElectionService>();
        var ending = new StoppingWatchElections(elections, failure: null);
        var singletons = new DefaultSingletonService(ending);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var candidate = new SingletonCandidate(session);
        var started = new int[1];

        var task = singletons
            .RunAsync(
                role,
                candidate,
                async (_, token) =>
                {
                    Interlocked.Increment(ref started[0]);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                null,
                cancellationToken
            )
            .AsTask();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        var view = await elections.ReadAsync(role, null, cancellationToken);
        var totalStarted = Volatile.Read(ref started[0]);

        Assert.Equal("The watch ended without a failure.", exception.Message);
        Assert.Equal(1, totalStarted);
        Assert.Null(view.Leader);
    }

    #endregion

    private sealed class StoppingWatchElections(
        IElectionService inner,
        CoordinationException? failure
    ) : IElectionService
    {
        public ValueTask<ElectionResult> TryLeadAsync(
            CoordinationKey role,
            ReadOnlyMemory<byte> value,
            LeaseTerms terms,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.TryLeadAsync(role, value, terms, options, cancellationToken);

        public ValueTask<bool> RenewAsync(
            Leadership leadership,
            TimeToLive timeToLive,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.RenewAsync(leadership, timeToLive, options, cancellationToken);

        public ValueTask<bool> ResignAsync(
            Leadership leadership,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.ResignAsync(leadership, options, cancellationToken);

        public ValueTask<ElectionView> ReadAsync(
            CoordinationKey role,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.ReadAsync(role, options, cancellationToken);

        public async IAsyncEnumerable<ElectionView> WatchAsync(
            CoordinationKey role,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            if (failure is not null)
            {
                throw failure;
            }

            yield break;
        }
    }
}
