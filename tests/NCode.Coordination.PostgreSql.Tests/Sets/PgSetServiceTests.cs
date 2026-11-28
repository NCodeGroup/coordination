// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Sets.Tests;

public sealed class PgSetServiceTests
{
    private static PgSetService CreateService(PgOfflineServer offline)
    {
        var server = offline.Server;
        var keyspaces = offline.Keyspaces;
        var schema = offline.Settings.Schema;

        return new PgSetService(server, keyspaces, schema);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_NullMember_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var duration = TimeSpan.FromSeconds(5);
        var timeToLive = new TimeToLive(duration);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.AddAsync(key, null!, timeToLive, null, cancellationToken)
        );
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions { Extensions = PgTestSupport.UnsupportedExtensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ListAsync(key, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
