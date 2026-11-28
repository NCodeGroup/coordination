// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using NCode.Coordination.PostgreSql.Keys;
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
            TimeProvider.System,
            NullLoggerFactory.Instance
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
        var options = new OperationOptions { Extensions = PgTestSupport.UnsupportedExtensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ReadAsync(group, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region Concerns Tests

    [Theory]
    [InlineData("watched", null, true)]
    [InlineData("other", null, false)]
    [InlineData(null, "s-1", true)]
    [InlineData(null, null, false)]
    public void Concerns_Entry_TellsWhetherTheViewMayHaveChanged(
        string? groupName,
        string? endedSession,
        bool expected
    )
    {
        var keyspace = new Keyspace("prod");
        var watched = keyspace.CreateKey("watched");
        var address = PgMembershipService.AddressOf(watched);
        byte[]? groupIndex = null;
        short? groupDepth = null;
        if (groupName is not null)
        {
            var group = keyspace.CreateKey(groupName);
            groupIndex = PgKeys.Index(group);
            groupDepth = (short)group.PartitionDepth;
        }

        var entry = new PgChange(1, endedSession, groupIndex, groupDepth, [], []);

        var concerns = PgMembershipService.Concerns(entry, address);

        Assert.Equal(expected, concerns);
    }

    [Fact]
    public void Concerns_NoEntry_ReturnsFalse()
    {
        var watched = new Keyspace("prod").CreateKey("watched");
        var address = PgMembershipService.AddressOf(watched);

        var concerns = PgMembershipService.Concerns(null, address);

        Assert.False(concerns);
    }

    #endregion
}
