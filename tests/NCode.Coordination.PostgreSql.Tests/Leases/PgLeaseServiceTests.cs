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

        return new PgLeaseService(grants, tokens);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

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
        var grant = new LeaseGrant(
            keyspace.CreateKey("r"),
            new FencingToken(1),
            new OwnerToken("o-1"),
            LeaseTerms.ForSession(new SessionId(keyspace, "s-1"))
        );

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.RenewAsync(
                grant,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                null,
                cancellationToken
            )
        );
    }

    #endregion
}
