// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// Proves that a PostgreSQL provider reports failures honestly when its server restarts, each test restarting a server
/// of its own.
/// </summary>
public sealed class PostgreSqlProviderRestartTests
{
    /// <summary>
    /// The name of the keyspaces that the tests create, which tells their keys apart from other tests'.
    /// </summary>
    private const string KeyspaceName = "restart";

    /// <summary>
    /// The number of clients that write concurrently while the server restarts.
    /// </summary>
    private const int Clients = 3;

    /// <summary>
    /// The number of records that each client writes.
    /// </summary>
    private const int Steps = 12;

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_WhenTheServerRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<(CoordinationKey Key, CoordinationException Failure)>();
        var reached = 0;

        async ValueTask RestartAsync()
        {
            await halfway.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
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

                    await stopped.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
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
        await PgTestSupport.WaitUntilAnsweredAsync(
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

    #endregion
}
