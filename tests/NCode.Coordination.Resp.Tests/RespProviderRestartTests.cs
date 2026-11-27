// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
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
}
