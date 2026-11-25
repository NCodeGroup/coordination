// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
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
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            events.Enqueue("start");
            await release.Task.WaitAsync(token);
            events.Enqueue("end");
        }

        var firstTask = singletons
            .RunAsync(role, firstCandidate, Work, null, cancellationToken)
            .AsTask();
        var secondTask = singletons
            .RunAsync(role, secondCandidate, Work, null, cancellationToken)
            .AsTask();

        await RecipeTesting.WaitUntilAsync(() => !events.IsEmpty);
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
        Leadership? firstLeadership = null;
        var firstToken = CancellationToken.None;

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls[0]) != 1)
            {
                return;
            }

            firstToken = token;
            Volatile.Write(ref firstLeadership, leadership);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var task = singletons.RunAsync(role, candidate, Work, null, cancellationToken).AsTask();

        Func<bool> hasLeadership = () => Volatile.Read(ref firstLeadership) is not null;
        await RecipeTesting.WaitUntilAsync(hasLeadership);
        var observed = Volatile.Read(ref firstLeadership);
        var leadership = Assert.IsType<Leadership>(observed);
        var resigned = await elections.ResignAsync(leadership, null, cancellationToken);
        var completed = await task;
        var totalCalls = Volatile.Read(ref calls[0]);

        Assert.True(resigned);
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

    #endregion
}
