// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Leases;
using NCode.Coordination.Resp.Membership;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Sessions.Tests;

public sealed class RespSessionServiceTests(RespServers servers)
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

    private static async Task AssertCloseRemovesWhatIsBoundAsync(
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
        var table = new RespRecordTable(new RespServer(connection), profile);
        var tokens = new RespRandomTokens();
        var sessions = new RespSessionService(table, tokens);
        var leases = new RespLeaseService(table, tokens);
        var records = new RespRecordService(table);
        var checkInterval = RespSubscriptions.DefaultCheckInterval;
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, profile, schedule);
        var membership = new RespMembershipService(table, subscriptions);
        var keyspace = CreateKeyspace();
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);
        var grant = await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);
        var resource = keyspace.CreateKey("resource");
        var terms = LeaseTerms.ForSession(grant.Id);
        var acquired = await leases.TryAcquireAsync(resource, terms, null, cancellationToken);
        Assert.True(acquired.IsAcquired);
        var record = keyspace.CreateKey("record");
        var value = "v"u8.ToArray();
        var lifetime = RecordLifetime.ForSession(grant.Id);
        var created = await records.CreateAsync(record, value, lifetime, null, cancellationToken);
        Assert.True(created.IsWritten);
        var member = new Member(grant.Id, "node-1", "address-1");
        var group = keyspace.CreateKey("group");
        await membership.JoinAsync(group, member, null, cancellationToken);
        var keys = RespRecordTable.Keys(keyspace);
        var database = multiplexer.GetDatabase();
        var bound = await database.SortedSetLengthAsync(keys[6]);
        Assert.Equal(3, bound);
        var before = (long)await database.StringGetAsync(keys[0]);

        await sessions.CloseAsync(grant.Id, null, cancellationToken);

        var bindings = await database.SortedSetLengthAsync(keys[6]);
        var index = await database.SortedSetLengthAsync(keys[2]);
        var members = await database.SortedSetLengthAsync(keys[10]);
        var leaseField = "l " + RespKeys.Name(resource);
        var leased = await database.HashExistsAsync(keys[7], leaseField);
        var after = (long)await database.StringGetAsync(keys[0]);
        Assert.Equal(0, bindings);
        Assert.Equal(0, index);
        Assert.Equal(0, members);
        Assert.False(leased);
        Assert.True(after > before);
    }

    private static async Task AssertCloseLeavesBindingsPast64Async(
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
        var table = new RespRecordTable(new RespServer(connection), profile);
        var tokens = new RespRandomTokens();
        var sessions = new RespSessionService(table, tokens);
        var records = new RespRecordService(table);
        var keyspace = CreateKeyspace();
        var minute = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(minute);
        var grant = await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);
        var value = "v"u8.ToArray();
        var lifetime = RecordLifetime.ForSession(grant.Id);
        for (var index = 0; index < 65; index++)
        {
            var key = keyspace.CreateKey("r", index);
            var created = await records.CreateAsync(key, value, lifetime, null, cancellationToken);
            Assert.True(created.IsWritten);
        }
        var keys = RespRecordTable.Keys(keyspace);
        var database = multiplexer.GetDatabase();
        var bound = await database.SortedSetLengthAsync(keys[6]);
        Assert.Equal(65, bound);

        await sessions.CloseAsync(grant.Id, null, cancellationToken);

        var bindingsAfterClose = await database.SortedSetLengthAsync(keys[6]);
        var indexAfterClose = await database.SortedSetLengthAsync(keys[2]);
        Assert.Equal(1, bindingsAfterClose);
        Assert.Equal(1, indexAfterClose);

        var other = keyspace.CreateKey("other");
        await records.ReadAsync(other, null, cancellationToken);

        var bindingsAfterRead = await database.SortedSetLengthAsync(keys[6]);
        var indexAfterRead = await database.SortedSetLengthAsync(keys[2]);
        Assert.Equal(0, bindingsAfterRead);
        Assert.Equal(0, indexAfterRead);
    }

    #region CloseAsync Tests

    [RespServerFact]
    public Task CloseAsync_Valkey_RemovesTheLeaseTheRecordAndTheMemberBoundToTheSession() =>
        AssertCloseRemovesWhatIsBoundAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task CloseAsync_Garnet_RemovesTheLeaseTheRecordAndTheMemberBoundToTheSession() =>
        AssertCloseRemovesWhatIsBoundAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task CloseAsync_Valkey_LeavesBindingsPast64ForTheNextScript() =>
        AssertCloseLeavesBindingsPast64Async(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task CloseAsync_Garnet_LeavesBindingsPast64ForTheNextScript() =>
        AssertCloseLeavesBindingsPast64Async(servers.Garnet, RespProfiles.Garnet);

    #endregion
}
