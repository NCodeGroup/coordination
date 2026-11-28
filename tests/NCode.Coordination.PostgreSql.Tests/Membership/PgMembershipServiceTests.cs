// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Membership.Tests;

public sealed class PgMembershipServiceTests
{
    private static PgMembershipService CreateService(PgOfflineServer offline)
    {
        var changeLog = new PgChangeLog(offline.Server, offline.Settings.Schema);
        var hub = new PgLogHub(
            offline.Source,
            offline.Keyspaces,
            offline.Settings,
            TimeProvider.System
        );

        return new PgMembershipService(offline.Table, changeLog, hub);
    }

    #region JoinAsync Tests

    [Fact]
    public async Task JoinAsync_NullMember_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var group = new Keyspace("prod").CreateKey("g");

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.JoinAsync(group, null!, null, cancellationToken)
        );

        Assert.Equal("member", exception.ParamName);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var group = new Keyspace("prod").CreateKey("g");
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ReadAsync(group, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
