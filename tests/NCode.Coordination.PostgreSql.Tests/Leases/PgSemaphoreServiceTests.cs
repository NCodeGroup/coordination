// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Leases.Tests;

public sealed class PgSemaphoreServiceTests
{
    private static PgSemaphoreService CreateService(PgOfflineServer offline)
    {
        var grants = new PgGrants(offline.Table);
        var tokens = new PgRandomTokens();
        var polling = new PgPolling(TimeProvider.System);

        return new PgSemaphoreService(grants, tokens, polling);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_CapacityZero_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.TryAcquireAsync(resource, 0, terms, null, cancellationToken)
        );

        Assert.Equal("capacity", exception.ParamName);
        Assert.Equal(0, exception.ActualValue);
    }

    [Fact]
    public async Task TryAcquireAsync_CapacityAboveTheLimit_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.TryAcquireAsync(
                resource,
                CoordinationLimits.MaxSemaphoreCapacity + 1,
                terms,
                null,
                cancellationToken
            )
        );

        Assert.Equal("capacity", exception.ParamName);
        Assert.Equal(CoordinationLimits.MaxSemaphoreCapacity + 1, exception.ActualValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(CoordinationLimits.MaxSemaphoreCapacity)]
    public async Task TryAcquireAsync_CapacityAtTheBounds_ReachesTheServer(int capacity)
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryAcquireAsync(resource, capacity, terms, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion
}
