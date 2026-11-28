// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgServerUnitTests
{
    #region Timeouts Tests

    [Fact]
    public async Task Timeouts_Settings_FormatsWholeMilliseconds()
    {
        await using var source = PgSource.FromConnectionString(PgOfflineServer.ConnectionString);
        var settings = PgSettings.Default with
        {
            StatementTimeout = TimeSpan.FromSeconds(5),
            LockTimeout = TimeSpan.FromMilliseconds(1.5),
            IdleInTransactionTimeout = TimeSpan.FromMilliseconds(250),
        };

        var server = new PgServer(source, settings);

        Assert.Equal(3, server.Timeouts.Arguments.Length);
        Assert.Equal("5000ms", server.Timeouts.Arguments[0].Value);
        Assert.Equal("2ms", server.Timeouts.Arguments[1].Value);
        Assert.Equal("250ms", server.Timeouts.Arguments[2].Value);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_ServerUnreachable_ThrowsUnavailableNotApplied()
    {
        await using var offline = new PgOfflineServer();
        var server = offline.Server;

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(
                [new PgStatement("SELECT 1")],
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
