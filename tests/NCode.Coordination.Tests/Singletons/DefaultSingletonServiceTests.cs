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
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var first = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var second = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var events = new ConcurrentQueue<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask Work(Leadership leadership, CancellationToken token)
        {
            events.Enqueue("start");
            await release.Task.WaitAsync(token);
            events.Enqueue("end");
        }

        var firstTask = singletons
            .RunAsync(role, new SingletonCandidate(first), Work, null, cancellationToken)
            .AsTask();
        var secondTask = singletons
            .RunAsync(role, new SingletonCandidate(second), Work, null, cancellationToken)
            .AsTask();

        await RecipeTesting.WaitUntilAsync(() => !events.IsEmpty);
        release.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, Assert.True);
        Assert.Equal(["start", "end", "start", "end"], events.ToArray());
    }

    [Fact]
    public async Task RunAsync_WhenTheLeadershipIsLost_CancelsTheWorkAndCampaignsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
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

        var task = singletons
            .RunAsync(role, new SingletonCandidate(session), Work, null, cancellationToken)
            .AsTask();

        await RecipeTesting.WaitUntilAsync(() => Volatile.Read(ref firstLeadership) is not null);
        var leadership = Assert.IsType<Leadership>(Volatile.Read(ref firstLeadership));
        var resigned = await elections.ResignAsync(leadership, null, cancellationToken);
        var completed = await task;

        Assert.True(resigned);
        Assert.True(completed);
        Assert.Equal(2, Volatile.Read(ref calls[0]));
        Assert.True(firstToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_WhenTheSessionHasEnded_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var elections = services.GetRequiredService<IElectionService>();
        var sessions = services.GetRequiredService<ISessionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);
        var calls = new int[1];

        var completed = await singletons.RunAsync(
            role,
            new SingletonCandidate(session),
            (_, _) =>
            {
                Interlocked.Increment(ref calls[0]);
                return ValueTask.CompletedTask;
            },
            null,
            cancellationToken
        );

        Assert.False(completed);
        Assert.Equal(0, Volatile.Read(ref calls[0]));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileLeading_ResignsAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var elections = services.GetRequiredService<IElectionService>();
        var singletons = new DefaultSingletonService(elections);
        var role = CreateRole();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = singletons
            .RunAsync(
                role,
                new SingletonCandidate(session),
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
