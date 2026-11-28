// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Logging;
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
        var subscriptions = new RespSubscriptions(
            connection,
            profile,
            schedule,
            NullLoggerFactory.Instance
        );
        var tokens = new RespRandomTokens();
        var sessions = new RespSessionService(table, tokens);
        var membership = new RespMembershipService(
            table,
            subscriptions,
            NullLogger<RespMembershipService>.Instance
        );

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

    private static async Task<MembershipView> FinishAsync(
        Task<bool> moving,
        IAsyncEnumerator<MembershipView> enumerator,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var moved = await moving.WaitAsync(timeout, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    private static async Task<MembershipView> NextAsync(
        IAsyncEnumerator<MembershipView> enumerator,
        CancellationToken cancellationToken
    )
    {
        var moving = enumerator.MoveNextAsync().AsTask();

        return await FinishAsync(moving, enumerator, cancellationToken);
    }

    // Proving that nothing is yielded takes a bounded wait: a second spans five 200 ms checks.
    private static async Task AssertNoViewWithinASecondAsync(
        Task<bool> moving,
        CancellationToken cancellationToken
    )
    {
        var second = TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            moving.WaitAsync(second, cancellationToken)
        );
    }

    private static async Task AssertAnotherGroupsChangeYieldsNoViewAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (sessions, membership, _) = CreateServices(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var grant = await OpenSessionAsync(sessions, keyspace, cancellationToken);
        var group = keyspace.CreateKey("group");
        var other = keyspace.CreateKey("other");
        var outsider = new Member(grant.Id, "node-2", "address-2");
        var member = new Member(grant.Id, "node-1", "address-1");
        var stream = membership.WatchAsync(group, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        var start = await NextAsync(enumerator, cancellationToken);
        Assert.Empty(start.Members);

        var moving = enumerator.MoveNextAsync().AsTask();
        var joinedOther = await membership.JoinAsync(other, outsider, null, cancellationToken);
        Assert.Equal(MembershipJoinStatus.Joined, joinedOther);
        await AssertNoViewWithinASecondAsync(moving, cancellationToken);
        var joined = await membership.JoinAsync(group, member, null, cancellationToken);
        Assert.Equal(MembershipJoinStatus.Joined, joined);
        var view = await FinishAsync(moving, enumerator, cancellationToken);

        var only = Assert.Single(view.Members);
        Assert.Equal("node-1", only.NodeId);
        var expected = new Revision(start.Revision.Value + 2);
        Assert.Equal(expected, view.Revision);
    }

    private static async Task AssertMalformedEventListsTheGroupAgainAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (_, membership, subscriptions) = CreateServices(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var group = keyspace.CreateKey("group");
        var keys = RespRecordTable.Keys(keyspace);
        var watchChannel = RespRecordTable.WatchChannel(keyspace);
        var channel = subscriptions.Channel(watchChannel);
        var database = multiplexer.GetDatabase();
        var subscriber = multiplexer.GetSubscriber();
        var stream = membership.WatchAsync(group, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        var start = await NextAsync(enumerator, cancellationToken);

        var next = start.Revision.Value + 1;
        var set = await database.StringSetAsync(keys[0], next);
        Assert.True(set);
        var number = next.ToString(CultureInfo.InvariantCulture);
        var malformed = number + "\nw";
        await subscriber.PublishAsync(channel, malformed);
        var view = await NextAsync(enumerator, cancellationToken);

        var expected = new Revision(next);
        Assert.Equal(expected, view.Revision);
        Assert.Empty(view.Members);
    }

    private static async Task AssertUnchangedResyncYieldsNoViewAsync(
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
        var member = new Member(grant.Id, "node-1", "address-1");
        var watchChannel = RespRecordTable.WatchChannel(keyspace);
        var channel = subscriptions.Channel(watchChannel);
        var subscriber = multiplexer.GetSubscriber();
        var stream = membership.WatchAsync(group, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        var start = await NextAsync(enumerator, cancellationToken);

        var ahead = start.Revision.Value + 1;
        var number = ahead.ToString(CultureInfo.InvariantCulture);
        var malformed = number + "\nw";
        var moving = enumerator.MoveNextAsync().AsTask();
        await subscriber.PublishAsync(channel, malformed);
        await AssertNoViewWithinASecondAsync(moving, cancellationToken);
        var joined = await membership.JoinAsync(group, member, null, cancellationToken);
        Assert.Equal(MembershipJoinStatus.Joined, joined);
        var view = await FinishAsync(moving, enumerator, cancellationToken);

        var only = Assert.Single(view.Members);
        Assert.Equal("node-1", only.NodeId);
        var expected = new Revision(ahead);
        Assert.Equal(expected, view.Revision);
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

    #region WatchAsync Tests

    [RespServerFact]
    public Task WatchAsync_Valkey_WhenAnotherGroupChanges_YieldsNoView() =>
        AssertAnotherGroupsChangeYieldsNoViewAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task WatchAsync_Garnet_WhenAnotherGroupChanges_YieldsNoView() =>
        AssertAnotherGroupsChangeYieldsNoViewAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task WatchAsync_Valkey_WhenAnEventIsMalformed_ListsTheGroupAgainAtACheck() =>
        AssertMalformedEventListsTheGroupAgainAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task WatchAsync_Garnet_WhenAnEventIsMalformed_ListsTheGroupAgainAtACheck() =>
        AssertMalformedEventListsTheGroupAgainAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task WatchAsync_Valkey_WhenAResyncFindsTheGroupUnchanged_YieldsNoView() =>
        AssertUnchangedResyncYieldsNoViewAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task WatchAsync_Garnet_WhenAResyncFindsTheGroupUnchanged_YieldsNoView() =>
        AssertUnchangedResyncYieldsNoViewAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion

    #region TryReadAsync Tests

    [Fact]
    public async Task TryReadAsync_WhenTheServerIsUnreachable_LogsTheReadFailureOnce()
    {
        var connection = new RespConnection(
            _ =>
                ValueTask.FromException<IConnectionMultiplexer>(
                    new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        CommandFlags.None,
                        "unreachable",
                        null,
                        CommandStatus.Unknown
                    )
                ),
            owned: false
        );
        var profile = RespProfiles.Valkey;
        var table = new RespRecordTable(new RespServer(connection), profile);
        var schedule = new RespCheckSchedule(TimeSpan.FromMinutes(1), TimeProvider.System);
        var subscriptions = new RespSubscriptions(
            connection,
            profile,
            schedule,
            NullLoggerFactory.Instance
        );
        var logger = new FakeLogger<RespMembershipService>();
        var membership = new RespMembershipService(table, subscriptions, logger);
        var group = CreateKeyspace().CreateKey("group");

        var view = await membership.TryReadAsync(group, TestContext.Current.CancellationToken);

        Assert.Null(view);
        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records);
        Assert.Equal(EventIds.WatchGroupReadFailed, record.Id.Id);
        Assert.Equal(LogLevel.Warning, record.Level);
    }

    #endregion
}
