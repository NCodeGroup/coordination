// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Counters.Tests;

public sealed class PgCounterServiceServerTests(PgServers servers)
{
    private static TimeSpan OneMinuteSpan { get; } = TimeSpan.FromMinutes(1);

    private static TimeToLive OneMinute { get; } = new(OneMinuteSpan);

    #region IncrementBatchAsync Tests

    [PgServerFact]
    public async Task IncrementBatchAsync_SumPassesTheLargestCount_ThrowsLimitExceededAndAppliesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await PgTestSupport.CreateProviderAsync(
            servers.PostgreSql19,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
        var counters = PgTestSupport.GetService<ICounterService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace("counters");
        var full = keyspace.CreateKey("full");
        var other = keyspace.CreateKey("other");
        await counters.IncrementAsync(full, long.MaxValue, OneMinute, null, cancellationToken);
        var first = new CounterIncrement(other, 1, long.MaxValue, OneMinute);
        var second = new CounterIncrement(full, 1, long.MaxValue, OneMinute);
        CounterIncrement[] batch = [first, second];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await counters.IncrementBatchAsync(batch, null, cancellationToken)
        );
        var otherCount = await counters.ReadAsync(other, null, cancellationToken);
        var fullCount = await counters.ReadAsync(full, null, cancellationToken);

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(0, otherCount);
        Assert.Equal(long.MaxValue, fullCount);
    }

    [PgServerFact]
    public async Task IncrementBatchAsync_SumStaysAtTheLargestCount_AppliesEveryIncrement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await PgTestSupport.CreateProviderAsync(
            servers.PostgreSql19,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
        var counters = PgTestSupport.GetService<ICounterService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace("counters");
        var full = keyspace.CreateKey("full");
        var other = keyspace.CreateKey("other");
        await counters.IncrementAsync(full, long.MaxValue - 1, OneMinute, null, cancellationToken);
        var first = new CounterIncrement(other, 1, long.MaxValue, OneMinute);
        var second = new CounterIncrement(full, 1, long.MaxValue, OneMinute);
        CounterIncrement[] batch = [first, second];

        var result = await counters.IncrementBatchAsync(batch, null, cancellationToken);
        var otherCount = await counters.ReadAsync(other, null, cancellationToken);
        var fullCount = await counters.ReadAsync(full, null, cancellationToken);

        long[] expected = [1, long.MaxValue];
        Assert.Equal<long>(expected, result.Counts);
        Assert.Null(result.ExceededIndex);
        Assert.Equal(1, otherCount);
        Assert.Equal(long.MaxValue, fullCount);
    }

    #endregion
}
