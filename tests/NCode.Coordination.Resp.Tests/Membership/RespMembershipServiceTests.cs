// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Sessions;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Membership.Tests;

public sealed class RespMembershipServiceTests(RespServers servers)
{
    private static async Task<ConnectionMultiplexer> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);

        return await connecting.WaitAsync(cancellationToken);
    }

    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("resp-tests", segment);
    }

    private static async Task<string> ReadMessageAsync(
        ChannelMessageQueue queue,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var reading = queue.ReadAsync(cancellationToken);
        var task = reading.AsTask();
        var message = await task.WaitAsync(timeout, cancellationToken);
        var text = (string?)message.Message;
        Assert.NotNull(text);

        return text;
    }

    private static async Task AssertJoinAndLeavePublishTheGroupAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var connection = new RespConnection(
            _ => ValueTask.FromResult<IConnectionMultiplexer>(multiplexer),
            owned: false
        );
        var server = new RespServer(connection);
        var table = new RespRecordTable(server, profile);
        var subscriptions = new RespSubscriptions(
            connection,
            profile,
            RespSubscriptions.DefaultCheckInterval,
            TimeProvider.System
        );
        var tokens = new RespRandomTokens();
        var sessions = new RespSessionService(table, tokens);
        var membership = new RespMembershipService(table, subscriptions);
        var keyspace = CreateKeyspace();
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);
        var grant = await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);
        var group = keyspace.CreateKey("group");
        var watchChannel = RespRecordTable.WatchChannel(keyspace);
        var channel = subscriptions.Channel(watchChannel);
        var subscriber = multiplexer.GetSubscriber();
        var queue = await subscriber.SubscribeAsync(channel);
        var member = new Member(grant.Id, "node-1", "address-1");

        await membership.JoinAsync(group, member, null, cancellationToken);
        var joined = await ReadMessageAsync(queue, cancellationToken);
        await membership.LeaveAsync(group, grant.Id, null, cancellationToken);
        var left = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();

        var digits = joined.Split('\n')[0];
        var revision = long.Parse(digits, CultureInfo.InvariantCulture);
        var name = RespKeys.Member(group);
        Assert.Equal($"{revision}\nm{name}", joined);
        Assert.Equal($"{revision + 1}\nm{name}", left);
    }

    #region JoinAsync Tests

    [RespServerFact]
    public Task JoinAsync_Valkey_PublishesTheGroupAndLeaveAsyncPublishesItAgain() =>
        AssertJoinAndLeavePublishTheGroupAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task JoinAsync_Garnet_PublishesTheGroupAndLeaveAsyncPublishesItAgain() =>
        AssertJoinAndLeavePublishTheGroupAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion
}
