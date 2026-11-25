// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.Recipes.Tests;

/// <summary>
/// Builds the in-process coordination services and the helpers the recipes' tests share.
/// </summary>
internal static class RecipeTesting
{
    /// <summary>
    /// The keyspace the recipes' tests work in.
    /// </summary>
    public static Keyspace Keyspace { get; } = new("recipes");

    /// <summary>
    /// Builds the coordination services over the in-process provider and the given clock.
    /// </summary>
    /// <param name="time">The clock the in-process provider expires on.</param>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider Build(FakeTimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(time);
        services.AddCoordination(b =>
            b.AddInProcessProvider()
                .Bind(new ProviderBinding(InProcessCoordinationBuilderExtensions.ProviderName))
        );
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Opens a session in <see cref="Keyspace"/> with a time to live of one minute.
    /// </summary>
    /// <param name="services">The services to open the session through.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The identifier of the new session.</returns>
    public static async Task<SessionId> OpenSessionAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var sessions = services.GetRequiredService<ISessionService>();
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var grant = await sessions.OpenAsync(Keyspace, timeToLive, null, cancellationToken);
        return grant.Id;
    }

    /// <summary>
    /// Starts the callers on separate threads, releases them together and waits for all of them.
    /// </summary>
    /// <typeparam name="TResult">The type each caller returns.</typeparam>
    /// <param name="count">The number of callers.</param>
    /// <param name="caller">The call each caller makes, given its index.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The results of the callers, in the order of their indexes.</returns>
    public static async Task<TResult[]> RunConcurrentlyAsync<TResult>(
        int count,
        Func<int, ValueTask<TResult>> caller,
        CancellationToken cancellationToken
    )
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task<TResult>[count];
        for (var index = 0; index < count; index++)
        {
            var callerIndex = index;
            tasks[index] = Task.Run(
                async () =>
                {
                    await gate.Task.WaitAsync(cancellationToken);
                    return await caller(callerIndex);
                },
                cancellationToken
            );
        }

        gate.SetResult();

        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Waits until the condition holds, and fails the test when it does not within 15 seconds.
    /// </summary>
    /// <param name="condition">The condition to poll.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(15))
            {
                Assert.Fail("The condition was not met within 15 seconds.");
            }

            await Task.Delay(10);
        }
    }
}
