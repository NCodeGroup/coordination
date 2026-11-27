// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Elections.Tests;

public sealed class PgElectionServiceTests
{
    private static async Task WithServiceAsync(Func<PgElectionService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);
        var table = new PgRecordTable(server, keyspaces, settings);

        await test(new PgElectionService(new PgGrants(table), new PgRandomTokens()));
    }

    #region TryLeadAsync Tests

    [Fact]
    public async Task TryLeadAsync_ValueTooLong_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var role = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));
        var value = new byte[CoordinationLimits.MaxValueLength + 1];
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.TryLeadAsync(role, value, terms, null, cancellationToken)
            );
        });
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_SessionBoundLeadership_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var leadership = new Leadership(
            new Leader(keyspace.CreateKey("r"), new FencingToken(1), new byte[] { 1 }),
            new OwnerToken("o-1"),
            LeaseTerms.ForSession(new SessionId(keyspace, "s-1"))
        );
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.RenewAsync(
                    leadership,
                    new TimeToLive(TimeSpan.FromSeconds(5)),
                    null,
                    cancellationToken
                )
            );
        });
    }

    #endregion
}
