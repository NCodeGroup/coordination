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

    private static (
        RespSessionService Sessions,
        RespMembershipService Membership,
        RespSubscriptions Subscriptions
    ) CreateServices(IConnectionMultiplexer multiplexer, RespProfile profile)
    {
        var connection = new RespConnection(_ => ValueTask.FromResult(multiplexer), owned: false);
        var server = new RespServer(connection);
        var table = new RespRecordTable(server, profile);
        var checkInterval = TimeSpan.FromMilliseconds(200);
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, profile, schedule);
        var tokens = new RespRandomTokens();
        var sessions = new RespSessionService(table, tokens);
        var membership = new RespMembershipService(table, subscriptions);

        return (sessions, membership, subscriptions);
    }

    private static async Task<SessionGrant> OpenSessionAsync(
        RespSessionService sessions,
        Keyspace keyspace,
        CancellationToken cancellationToken
    )
    {
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);

        return await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);
    }

    private static async Task<ChannelMessageQueue> SubscribeAsync(
        ConnectionMultiplexer multiplexer,
        RespSubscriptions subscriptions,
        Keyspace keyspace
    )
    {
        var watchChannel = RespRecordTable.WatchChannel(keyspace);
        var channel = subscriptions.Channel(watchChannel);
        var subscriber = multiplexer.GetSubscriber();

        return await subscriber.SubscribeAsync(channel);
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

    private static long ReadRevision(string message)
    {
        var digits = message.Split('\n')[0];

        return long.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static async Task AssertJoinPublishesTheGroupAsync(string endpoint, RespProfile profile)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (sessions, membership, subscriptions) = CreateServices(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var grant = await OpenSessionAsync(sessions, keyspace, cancellationToken);
        var group = keyspace.CreateKey("group");
        var queue = await SubscribeAsync(multiplexer, subscriptions, keyspace);
        var member = new Member(grant.Id, "node-1", "address-1");

        await membership.JoinAsync(group, member, null, cancellationToken);
        var joined = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();

        var revision = ReadRevision(joined);
        var name = RespKeys.Member(group);
        Assert.Equal($"{revision}\nm{name}", joined);
    }

    private static async Task AssertLeavePublishesTheGroupAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (sessions, membership, subscriptions) = CreateServices(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var grant = await OpenSessionAsync(sessions, keyspace, cancellationToken);
        var group = keyspace.CreateKey("group");
        var queue = await SubscribeAsync(multiplexer, subscriptions, keyspace);
        var member = new Member(grant.Id, "node-1", "address-1");
        await membership.JoinAsync(group, member, null, cancellationToken);
        var joined = await ReadMessageAsync(queue, cancellationToken);

        await membership.LeaveAsync(group, grant.Id, null, cancellationToken);
        var left = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();

        var revision = ReadRevision(joined);
        var name = RespKeys.Member(group);
        Assert.Equal($"{revision + 1}\nm{name}", left);
    }

    #region JoinAsync Tests

    [RespServerFact]
    public Task JoinAsync_Valkey_WhenTheMemberJoins_PublishesTheGroup() =>
        AssertJoinPublishesTheGroupAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task JoinAsync_Garnet_WhenTheMemberJoins_PublishesTheGroup() =>
        AssertJoinPublishesTheGroupAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion

    #region LeaveAsync Tests

    [RespServerFact]
    public Task LeaveAsync_Valkey_WhenTheMemberLeaves_PublishesTheGroup() =>
        AssertLeavePublishesTheGroupAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task LeaveAsync_Garnet_WhenTheMemberLeaves_PublishesTheGroup() =>
        AssertLeavePublishesTheGroupAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion
}
