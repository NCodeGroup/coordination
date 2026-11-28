// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Threading.Channels;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgListenerServerTests(PgServers servers)
{
    private static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    private static string NewChannel()
    {
        var hex = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        return $"ncode.{hex[..8]}.{hex[8..24]}";
    }

    private static async Task<(string Channel, string Payload)> ReadAsync(
        ChannelReader<(string Channel, string Payload)> reader,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);
        return await reader.ReadAsync(timeout.Token);
    }

    private string NewListenerConnectionString(string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(servers.Admin19)
        {
            ApplicationName = applicationName,
        };
        return builder.ConnectionString;
    }

    private async Task NotifyAsync(
        string channel,
        string payload,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(servers.Admin19);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT pg_notify($1, $2)", connection)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = channel },
                new NpgsqlParameter { Value = payload },
            },
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TerminateAsync(string applicationName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(servers.Admin19);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity "
                + "WHERE application_name = $1 AND pid <> pg_backend_pid()",
            connection
        )
        {
            Parameters = { new NpgsqlParameter { Value = applicationName } },
        };
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var terminated = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        Assert.True(terminated > 0);
    }

    #region ListenAsync Tests

    [PgServerFact]
    public async Task ListenAsync_ThenNotify_DeliversChannelAndPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = NewChannel();
        var received = Channel.CreateUnbounded<(string Channel, string Payload)>();

        var events = new PgListenerEvents(
            (c, p) => received.Writer.TryWrite((c, p)),
            () => { },
            () => { }
        );

        var applicationName = "listener-" + Guid.NewGuid().ToString("N");
        var connectionString = NewListenerConnectionString(applicationName);
        await using var source = PgSource.FromConnectionString(connectionString);
        await using var listener = new PgListener(source, events, TimeProvider.System);

        await listener.ListenAsync(channel, cancellationToken);
        await NotifyAsync(channel, "c:7", cancellationToken);

        var notification = await ReadAsync(received.Reader, cancellationToken);
        Assert.Equal(channel, notification.Channel);
        Assert.Equal("c:7", notification.Payload);
    }

    [PgServerFact]
    public async Task ListenAsync_ServerStoppedAfterTheFirstListen_ThrowsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = NewChannel();
        var second = NewChannel();
        var events = new PgListenerEvents((_, _) => { }, () => { }, () => { });

        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        await using var source = PgSource.FromConnectionString(server.ConnectionString);
        await using var listener = new PgListener(source, events, TimeProvider.System);

        await listener.ListenAsync(first, cancellationToken);
        await server.StopAsync(cancellationToken);

        var listening = listener.ListenAsync(second, cancellationToken).AsTask();
        var exception = await Assert.ThrowsAsync<CoordinationException>(() =>
            listening.WaitAsync(Patience, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion

    #region Unlisten Tests

    [PgServerFact]
    public async Task Unlisten_LastReference_StopsDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = NewChannel();
        var sentinel = NewChannel();
        var received = Channel.CreateUnbounded<(string Channel, string Payload)>();

        var events = new PgListenerEvents(
            (c, p) => received.Writer.TryWrite((c, p)),
            () => { },
            () => { }
        );

        var applicationName = "listener-" + Guid.NewGuid().ToString("N");
        var connectionString = NewListenerConnectionString(applicationName);
        await using var source = PgSource.FromConnectionString(connectionString);
        await using var listener = new PgListener(source, events, TimeProvider.System);

        await listener.ListenAsync(channel, cancellationToken);
        await NotifyAsync(channel, "c:1", cancellationToken);
        var first = await ReadAsync(received.Reader, cancellationToken);
        Assert.Equal(channel, first.Channel);

        listener.Unlisten(channel);

        // The request to listen to the sentinel queues behind the UNLISTEN, so once it completes the UNLISTEN ran.
        await listener.ListenAsync(sentinel, cancellationToken);
        await NotifyAsync(channel, "c:2", cancellationToken);
        await NotifyAsync(sentinel, "c:3", cancellationToken);

        var next = await ReadAsync(received.Reader, cancellationToken);
        Assert.Equal(sentinel, next.Channel);
        Assert.Equal("c:3", next.Payload);
        var drained = received.Reader.TryRead(out _);
        Assert.False(drained);
    }

    #endregion

    #region Reconnect Tests

    [PgServerFact]
    public async Task ListenAsync_BackendTerminated_ReconnectsListensAgainAndCallsReconnected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = NewChannel();
        var applicationName = "listener-" + Guid.NewGuid().ToString("N");
        var received = Channel.CreateUnbounded<(string Channel, string Payload)>();
        var reconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var identityChanges = 0;

        var events = new PgListenerEvents(
            (c, p) => received.Writer.TryWrite((c, p)),
            () => reconnected.TrySetResult(),
            () => Interlocked.Increment(ref identityChanges)
        );

        var connectionString = NewListenerConnectionString(applicationName);
        await using var source = PgSource.FromConnectionString(connectionString);
        await using var listener = new PgListener(source, events, TimeProvider.System);

        await listener.ListenAsync(channel, cancellationToken);
        await TerminateAsync(applicationName, cancellationToken);

        await reconnected.Task.WaitAsync(Patience, cancellationToken);

        await NotifyAsync(channel, "c:9", cancellationToken);
        var notification = await ReadAsync(received.Reader, cancellationToken);
        Assert.Equal(channel, notification.Channel);
        Assert.Equal("c:9", notification.Payload);

        var changes = Volatile.Read(ref identityChanges);
        Assert.Equal(0, changes);
        Assert.False(listener.HasSeenAnotherServer);
    }

    #endregion
}
