// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases.Tests;

public sealed class PgSemaphoreServiceTests
{
    private static async Task WithServiceAsync(Func<PgSemaphoreService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);
        var table = new PgRecordTable(server, keyspaces, settings);

        await test(new PgSemaphoreService(new PgGrants(table), new PgRandomTokens()));
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_CapacityZero_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.TryAcquireAsync(resource, 0, terms, null, cancellationToken)
            );
        });
    }

    [Fact]
    public async Task TryAcquireAsync_CapacityAboveTheLimit_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.TryAcquireAsync(
                    resource,
                    CoordinationLimits.MaxSemaphoreCapacity + 1,
                    terms,
                    null,
                    cancellationToken
                )
            );
        });
    }

    #endregion
}
