// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Sessions.Tests;

public sealed class PgSessionServiceTests
{
    private static PgSessionService CreateService(PgOfflineServer offline)
    {
        var table = offline.Table;
        var tokens = new PgRandomTokens();
        var polling = new PgPolling(TimeProvider.System);

        return new PgSessionService(table, tokens, polling);
    }

    #region OpenAsync Tests

    [Fact]
    public async Task OpenAsync_NullKeyspace_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.OpenAsync(
                null!,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                null,
                cancellationToken
            )
        );
    }

    #endregion

    #region KeepAliveAsync Tests

    [Fact]
    public async Task KeepAliveAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new SessionId(new Keyspace("prod"), "s-1");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.KeepAliveAsync(session, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
