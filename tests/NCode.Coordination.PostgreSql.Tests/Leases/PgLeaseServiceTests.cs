// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Leases.Tests;

public sealed class PgLeaseServiceTests
{
    private static PgLeaseService CreateService(PgOfflineServer offline)
    {
        var grants = new PgGrants(offline.Table);
        var tokens = new PgRandomTokens();
        var polling = new PgPolling(TimeProvider.System);

        return new PgLeaseService(grants, tokens, polling);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryAcquireAsync(resource, terms, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_SessionBoundGrant_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var resource = keyspace.CreateKey("r");
        var fencingToken = new FencingToken(1);
        var owner = new OwnerToken("o-1");
        var sessionId = new SessionId(keyspace, "s-1");
        var terms = LeaseTerms.ForSession(sessionId);
        var grant = new LeaseGrant(resource, fencingToken, owner, terms);
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.RenewAsync(grant, timeToLive, null, cancellationToken)
        );

        Assert.Equal("grant", exception.ParamName);
    }

    #endregion
}
