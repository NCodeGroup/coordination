// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Proves that a RESP provider reports failures honestly and resubscribes when its server restarts, each test restarting
/// a server of its own.
/// </summary>
public sealed class RespProviderRestartTests
{
    /// <summary>
    /// The number of clients that write concurrently while the server restarts.
    /// </summary>
    private const int Clients = 3;

    /// <summary>
    /// The number of records that each client writes.
    /// </summary>
    private const int Steps = 12;

    /// <summary>
    /// The name of the record prefix that the tests watch.
    /// </summary>
    private const string WatchedName = "watched";

    /// <summary>
    /// The number of rounds that a test waits for a subscription to resync and deliver again after a restart.
    /// </summary>
    private const int Attempts = 20;

    /// <summary>
    /// Gets how long a test waits for a restarted server to answer or for a restart step to happen.
    /// </summary>
    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a keyspace with a unique id, so that no other test sees its keys.
    /// </summary>
    /// <returns>The keyspace.</returns>
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("restart", segment);
    }

    /// <summary>
    /// Gets a primitive service of the provider, failing the test when the provider offers none.
    /// </summary>
    /// <typeparam name="TService">The service interface to get.</typeparam>
    /// <param name="provider">The provider that offers the service.</param>
    /// <returns>The service.</returns>
    private static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    /// <summary>
    /// Repeats a call until it stops failing with a coordination exception, which is how a client sees a server that
    /// has not yet come back, and fails the test when the server stays silent for too long.
    /// </summary>
    /// <param name="call">The call to repeat.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the call succeeds.</returns>
    private static async ValueTask WaitUntilAnsweredAsync(
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var pause = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try
            {
                await call(cancellationToken);
                return;
            }
            catch (CoordinationException exception)
            {
                if (stopwatch.Elapsed > WaitLimit)
                    Assert.Fail(
                        $"The server did not answer within {WaitLimit}: {exception.Message}"
                    );

                await Task.Delay(pause, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Moves an enumerator to its next item, failing the test when none arrives in time.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="enumerator">The enumerator to move.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The next item.</returns>
    private static async ValueTask<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(timeout, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    /// <summary>
    /// Restarts a server while clients write records and checks that every failure is unavailable and that no write
    /// reported as unapplied is stored.
    /// </summary>
    /// <param name="image">The image of the server to restart.</param>
    /// <param name="command">The arguments of the server's command, empty to run the image's default.</param>
    /// <param name="profile">The profile of the provider that writes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertWritesAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await RespRestartableServer.StartAsync(
            image,
            command,
            cancellationToken
        );
        var target = await RespConformance.CreateTargetAsync(
            server.Endpoint,
            profile,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var records = GetService<IRecordService>(target.Provider);
        var keyspace = CreateKeyspace();
        var value = "v"u8.ToArray();
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<(CoordinationKey Key, CoordinationException Failure)>();
        var reached = 0;

        async ValueTask RestartAsync()
        {
            await halfway.Task.WaitAsync(WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(WaitLimit, cancellationToken);
            await server.StartAgainAsync(cancellationToken);
        }

        async ValueTask ClientAsync(int client)
        {
            for (var step = 0; step < Steps; step++)
            {
                if (step == Steps / 2)
                {
                    if (Interlocked.Increment(ref reached) == Clients)
                        halfway.TrySetResult();

                    await stopped.Task.WaitAsync(WaitLimit, cancellationToken);
                }

                var key = keyspace.CreateKey("w", client, step);
                try
                {
                    await records.CreateAsync(
                        key,
                        value,
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                }
                catch (CoordinationException exception)
                {
                    failures.Enqueue((key, exception));
                    failed.TrySetResult();
                }
            }
        }

        var restarting = RestartAsync().AsTask();
        List<Task> running = [restarting];
        for (var client = 0; client < Clients; client++)
        {
            var writer = ClientAsync(client).AsTask();
            running.Add(writer);
        }

        await Task.WhenAll(running);

        var afterKey = keyspace.CreateKey("after");
        await WaitUntilAnsweredAsync(
            async ct =>
            {
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct);
            },
            cancellationToken
        );

        Assert.NotEmpty(failures);
        foreach (var (key, failure) in failures)
        {
            Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
            if (!failure.OutcomeUnknown)
            {
                var stored = await records.ReadAsync(key, null, cancellationToken);
                Assert.Null(stored);
            }
        }

        var after = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(after);
    }

    /// <summary>
    /// Restarts a server while a subscription and a record watch are open and checks that each resyncs and then
    /// delivers later messages and changes.
    /// </summary>
    /// <param name="image">The image of the server to restart.</param>
    /// <param name="command">The arguments of the server's command, empty to run the image's default.</param>
    /// <param name="profile">The profile of the provider that subscribes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertSubscriptionsAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await RespRestartableServer.StartAsync(
            image,
            command,
            cancellationToken
        );
        var target = await RespConformance.CreateTargetAsync(
            server.Endpoint,
            profile,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var notifications = GetService<INotificationService>(target.Provider);
        var records = GetService<IRecordService>(target.Provider);
        var watches = GetService<IRecordWatchService>(target.Provider);
        var keyspace = CreateKeyspace();
        var value = "v"u8.ToArray();
        var topic = keyspace.CreateKey("topic");
        var prefix = keyspace.CreateKey(WatchedName);

        var messages = notifications.SubscribeAsync(topic, null, cancellationToken);
        await using var messageEnumerator = messages.GetAsyncEnumerator(cancellationToken);
        var changes = watches.WatchPrefixAsync(prefix, null, null, cancellationToken);
        await using var changeEnumerator = changes.GetAsyncEnumerator(cancellationToken);

        var messageStart = await NextAsync(messageEnumerator, cancellationToken);
        Assert.True(messageStart.IsResync);
        var changeStart = await NextAsync(changeEnumerator, cancellationToken);
        Assert.True(changeStart.IsResync);

        var beforePayload = "before"u8.ToArray();
        await notifications.PublishAsync(topic, beforePayload, null, cancellationToken);
        var beforeKey = keyspace.CreateKey(WatchedName, "before");
        await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var beforeMessage = await NextAsync(messageEnumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, beforeMessage.Kind);
        var beforeReceived = beforeMessage.Payload.ToArray();
        Assert.Equal(beforePayload, beforeReceived);
        var beforeChange = await NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, beforeChange.Kind);
        Assert.Equal(beforeKey, beforeChange.Key);

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

        var resynced = false;
        var delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var payload = Encoding.UTF8.GetBytes($"after-{attempt}");
            await WaitUntilAnsweredAsync(
                ct => notifications.PublishAsync(topic, payload, null, ct),
                cancellationToken
            );

            var notification = await NextAsync(messageEnumerator, cancellationToken);
            if (notification.IsResync)
                resynced = true;
            else
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);

        resynced = false;
        delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var key = keyspace.CreateKey(WatchedName, $"after-{attempt}");
            await WaitUntilAnsweredAsync(
                async ct =>
                {
                    await records.CreateAsync(key, value, RecordLifetime.Persistent, null, ct);
                },
                cancellationToken
            );

            var change = await NextAsync(changeEnumerator, cancellationToken);
            if (change.IsResync)
                resynced = true;
            else if (change.Kind == RecordChangeKind.Written)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);
    }

    #region CreateAsync Tests

    [RespServerFact]
    public async Task CreateAsync_WhenValkeyRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        await AssertWritesAcrossARestartAsync(
            RespServers.ValkeyImage,
            [],
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task CreateAsync_WhenGarnetRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        await AssertWritesAcrossARestartAsync(
            RespServers.GarnetImage,
            RespServers.GarnetCommand,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public async Task SubscribeAsync_WhenValkeyRestarts_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossARestartAsync(
            RespServers.ValkeyImage,
            [],
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task SubscribeAsync_WhenGarnetRestarts_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossARestartAsync(
            RespServers.GarnetImage,
            RespServers.GarnetCommand,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion
}
