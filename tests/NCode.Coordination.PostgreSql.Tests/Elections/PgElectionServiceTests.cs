// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Leases;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Elections.Tests;

public sealed class PgElectionServiceTests
{
    private static PgElectionService CreateService(PgOfflineServer offline)
    {
        var grants = new PgGrants(offline.Table);
        var tokens = new PgRandomTokens();
        var polling = new PgPolling(TimeProvider.System);

        return new PgElectionService(grants, tokens, polling);
    }

    #region TryLeadAsync Tests

    [Fact]
    public async Task TryLeadAsync_ValueTooLong_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var role = new Keyspace("prod").CreateKey("r");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.TryLeadAsync(role, value, terms, null, cancellationToken)
        );

        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_SessionBoundLeadership_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var role = keyspace.CreateKey("r");
        var fencingToken = new FencingToken(1);
        byte[] value = [1];
        var leader = new Leader(role, fencingToken, value);
        var owner = new OwnerToken("o-1");
        var sessionId = new SessionId(keyspace, "s-1");
        var terms = LeaseTerms.ForSession(sessionId);
        var leadership = new Leadership(leader, owner, terms);
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.RenewAsync(leadership, timeToLive, null, cancellationToken)
        );

        Assert.Equal("leadership", exception.ParamName);
    }

    #endregion
}
