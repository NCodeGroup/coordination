// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Leases.Tests;

public sealed class PgLeaseServiceTests
{
    private static async Task WithServiceAsync(Func<PgLeaseService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);
        var table = new PgRecordTable(server, keyspaces, settings);

        await test(new PgLeaseService(new PgGrants(table), new PgRandomTokens()));
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = new Keyspace("prod").CreateKey("r");
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(5)));
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.TryAcquireAsync(resource, terms, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_SessionBoundGrant_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var grant = new LeaseGrant(
            keyspace.CreateKey("r"),
            new FencingToken(1),
            new OwnerToken("o-1"),
            LeaseTerms.ForSession(new SessionId(keyspace, "s-1"))
        );
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.RenewAsync(
                    grant,
                    new TimeToLive(TimeSpan.FromSeconds(5)),
                    null,
                    cancellationToken
                )
            );
        });
    }

    #endregion
}
