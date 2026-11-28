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

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.IncrementAsync(
                key,
                0,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                null,
                cancellationToken
            )
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
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(
                key,
                1,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                options,
                cancellationToken
            )
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
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(5));
        CounterIncrement[] increments =
        [
            new CounterIncrement(new Keyspace("prod").CreateKey("a"), 1, 10, timeToLive),
            new CounterIncrement(new Keyspace("staging").CreateKey("b"), 1, 10, timeToLive),
        ];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.IncrementBatchAsync(increments, null, cancellationToken)
        );

        Assert.Equal("increments", exception.ParamName);
    }

    #endregion
}
