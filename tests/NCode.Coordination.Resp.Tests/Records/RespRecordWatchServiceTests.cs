// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records.Tests;

public sealed class RespRecordWatchServiceTests(RespServers servers)
{
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("resp-tests", segment);
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);

        return await connecting.WaitAsync(cancellationToken);
    }

    private static (RespRecordService Records, RespRecordWatchService Watches) CreateServices(
        IConnectionMultiplexer multiplexer,
        RespProfile profile
    )
    {
        var connection = new RespConnection(_ => ValueTask.FromResult(multiplexer), owned: false);

        var server = new RespServer(connection);
        var table = new RespRecordTable(server, profile);
        var checkInterval = TimeSpan.FromMilliseconds(200);
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, profile, schedule);
        var records = new RespRecordService(table);

        return (records, new RespRecordWatchService(table, records, subscriptions));
    }

    private static async Task<RecordChange> NextAsync(
        IAsyncEnumerator<RecordChange> enumerator,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(timeout, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    private static async Task AssertExpiredRecordYieldsTheDeleteAtACheckAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (records, watches) = CreateServices(multiplexer, profile);
        var key = CreateKeyspace().CreateKey("a");
        var second = TimeSpan.FromSeconds(1);
        var timeToLive = new TimeToLive(second);
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);
        var value = "v"u8.ToArray();
        var created = await records.CreateAsync(key, value, lifetime, null, cancellationToken);
        Assert.True(created.IsWritten);
        var stream = watches.WatchKeyAsync(key, null, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        var start = await NextAsync(enumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Resync, start.Kind);
        var change = await NextAsync(enumerator, cancellationToken);

        Assert.Equal(RecordChangeKind.Deleted, change.Kind);
        Assert.Equal(key, change.Key);
        Assert.True(change.Revision > created.Revision);
    }

    #region WatchKeyAsync Tests

    [RespServerFact]
    public Task WatchKeyAsync_Valkey_WhenTheRecordExpires_YieldsTheDeleteAtACheck() =>
        AssertExpiredRecordYieldsTheDeleteAtACheckAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task WatchKeyAsync_Garnet_WhenTheRecordExpires_YieldsTheDeleteAtACheck() =>
        AssertExpiredRecordYieldsTheDeleteAtACheckAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion
}
