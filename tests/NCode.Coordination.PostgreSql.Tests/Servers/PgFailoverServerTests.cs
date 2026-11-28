// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

public sealed class PgFailoverServerTests
{
    private static async ValueTask<string> ReadSyncStateAsync(
        string connectionString,
        string expected,
        CancellationToken cancellationToken
    )
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT sync_state FROM pg_stat_replication",
            connection
        );

        var state = "";
        var limitDuration = TimeSpan.FromSeconds(30);
        var pollInterval = TimeSpan.FromMilliseconds(100);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(limitDuration);
        while (state != expected && !limit.IsCancellationRequested)
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            state = value as string ?? "";
            if (state != expected)
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
        }

        return state;
    }

    #region StartAsync Tests

    [PgServerFact]
    public async Task StartAsync_Asynchronous_StreamsToTheStandby()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);

        var state = await ReadSyncStateAsync(server.PrimaryAdmin, "async", cancellationToken);

        Assert.Equal("async", state);
    }

    [PgServerFact]
    public async Task StartAsync_Synchronous_ReplicatesSynchronously()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(true, cancellationToken);

        var state = await ReadSyncStateAsync(server.PrimaryAdmin, "sync", cancellationToken);

        Assert.Equal("sync", state);
    }

    #endregion
}
