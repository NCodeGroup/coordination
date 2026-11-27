// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;

namespace NCode.Coordination.Resp.Tests;

/// <summary>Each test restarts a server of its own.</summary>
public sealed class RespProviderRestartTests
{
    private const int Clients = 3;

    private const int Steps = 12;

    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    private static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    private static async Task WaitUntilAnsweredAsync(
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
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

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
    }

    private static async Task<T> NextAsync<T>(
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

    private static async Task AssertWritesAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
        var keyspace = new Keyspace("restart", KeySegment.FromGuid(Guid.NewGuid()));
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<(CoordinationKey Key, CoordinationException Failure)>();
        var reached = 0;

        async Task RestartAsync()
        {
            await halfway.Task.WaitAsync(WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(WaitLimit, cancellationToken);
            await server.StartAgainAsync(cancellationToken);
        }

        async Task ClientAsync(int client)
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
                        "v"u8.ToArray(),
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

        var restarting = RestartAsync();
        var writing = Enumerable.Range(0, Clients).Select(ClientAsync).ToArray();
        await Task.WhenAll([restarting, .. writing]);

        var afterKey = keyspace.CreateKey("after");
        await WaitUntilAnsweredAsync(
            async ct =>
            {
                await records.CreateAsync(
                    afterKey,
                    "v"u8.ToArray(),
                    RecordLifetime.Persistent,
                    null,
                    ct
                );
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

    private static async Task AssertSubscriptionsAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
        var keyspace = new Keyspace("restart", KeySegment.FromGuid(Guid.NewGuid()));
        var topic = keyspace.CreateKey("topic");
        var prefix = keyspace.CreateKey("watched");

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
        var beforeKey = keyspace.CreateKey("watched", "before");
        await records.CreateAsync(
            beforeKey,
            "v"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var beforeMessage = await NextAsync(messageEnumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, beforeMessage.Kind);
        Assert.Equal(beforePayload, beforeMessage.Payload.ToArray());
        var beforeChange = await NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, beforeChange.Kind);
        Assert.Equal(beforeKey, beforeChange.Key);

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

        var resynced = false;
        var delivered = false;
        for (var attempt = 0; attempt < 20 && !(resynced && delivered); attempt++)
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
        for (var attempt = 0; attempt < 20 && !(resynced && delivered); attempt++)
        {
            var key = keyspace.CreateKey("watched", $"after-{attempt}");
            await WaitUntilAnsweredAsync(
                async ct =>
                {
                    await records.CreateAsync(
                        key,
                        "v"u8.ToArray(),
                        RecordLifetime.Persistent,
                        null,
                        ct
                    );
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
    public Task CreateAsync_WhenValkeyRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        AssertWritesAcrossARestartAsync(RespServers.ValkeyImage, [], RespProfiles.Valkey);

    [RespServerFact]
    public Task CreateAsync_WhenGarnetRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        AssertWritesAcrossARestartAsync(
            RespServers.GarnetImage,
            ["--lua", "--lua-transaction-mode", "true"],
            RespProfiles.Garnet
        );

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public Task SubscribeAsync_WhenValkeyRestarts_ResyncsAndDeliversLaterChanges() =>
        AssertSubscriptionsAcrossARestartAsync(RespServers.ValkeyImage, [], RespProfiles.Valkey);

    [RespServerFact]
    public Task SubscribeAsync_WhenGarnetRestarts_ResyncsAndDeliversLaterChanges() =>
        AssertSubscriptionsAcrossARestartAsync(
            RespServers.GarnetImage,
            ["--lua", "--lua-transaction-mode", "true"],
            RespProfiles.Garnet
        );

    #endregion
}
