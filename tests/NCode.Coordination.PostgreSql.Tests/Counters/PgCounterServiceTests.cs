// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Counters.Tests;

public sealed class PgCounterServiceTests
{
    private static PgCounterService CreateService(PgOfflineServer offline)
    {
        var server = offline.Server;
        var keyspaces = offline.Keyspaces;
        var schema = offline.Settings.Schema;

        return new PgCounterService(server, keyspaces, schema);
    }

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_AmountBelowOne_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.IncrementAsync(key, 0, timeToLive, null, cancellationToken)
        );

        Assert.Equal("amount", exception.ParamName);
    }

    [Fact]
    public async Task IncrementAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(key, 1, timeToLive, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region IncrementBatchAsync Tests

    [Fact]
    public async Task IncrementBatchAsync_Empty_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.IncrementBatchAsync([], null, cancellationToken)
        );

        Assert.Equal("increments", exception.ParamName);
    }

    [Fact]
    public async Task IncrementBatchAsync_KeyspacesDiffer_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var prod = new Keyspace("prod");
        var staging = new Keyspace("staging");
        var prodKey = prod.CreateKey("a");
        var stagingKey = staging.CreateKey("b");
        CounterIncrement[] increments =
        [
            new CounterIncrement(prodKey, 1, 10, timeToLive),
            new CounterIncrement(stagingKey, 1, 10, timeToLive),
        ];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.IncrementBatchAsync(increments, null, cancellationToken)
        );

        Assert.Equal("increments", exception.ParamName);
    }

    #endregion
}
