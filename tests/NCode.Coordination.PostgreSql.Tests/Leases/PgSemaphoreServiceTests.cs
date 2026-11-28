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

        return new PgSemaphoreService(grants, tokens);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_CapacityZero_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.TryAcquireAsync(resource, 0, terms, null, cancellationToken)
        );
    }

    [Fact]
    public async Task TryAcquireAsync_CapacityAboveTheLimit_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.TryAcquireAsync(
                resource,
                CoordinationLimits.MaxSemaphoreCapacity + 1,
                terms,
                null,
                cancellationToken
            )
        );
    }

    #endregion
}
