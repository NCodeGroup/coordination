// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgServerUnitTests
{
    #region Timeouts Tests

    [Fact]
    public async Task Timeouts_Settings_FormatsWholeMilliseconds()
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p"
        );
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
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);

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
