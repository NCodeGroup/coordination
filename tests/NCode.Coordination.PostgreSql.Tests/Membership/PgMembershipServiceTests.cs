// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Membership.Tests;

public sealed class PgMembershipServiceTests
{
    private static async Task WithServiceAsync(Func<PgMembershipService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);
        var table = new PgRecordTable(server, keyspaces, settings);
        await using var hub = new PgLogHub(source, keyspaces, settings, TimeProvider.System);
        var changeLog = new PgChangeLog(server, settings.Schema);

        await test(new PgMembershipService(table, changeLog, hub));
    }

    #region JoinAsync Tests

    [Fact]
    public async Task JoinAsync_NullMember_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var group = new Keyspace("prod").CreateKey("g");
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.JoinAsync(group, null!, null, cancellationToken)
            );
        });
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var group = new Keyspace("prod").CreateKey("g");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.ReadAsync(group, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
