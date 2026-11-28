// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Schema.Tests;

public sealed class PgProbeTests
{
    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_MultiplexingDataSource_ThrowsUnsupportedBeforeConnecting()
    {
        const string expected =
            "The provider's data source multiplexes commands, which its listening connection and SET LOCAL can't share; turn Multiplexing off.";
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Multiplexing=true"
        );
        var server = new PgServer(source, PgSettings.Default);
        var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, PgSettings.Default);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await probe.RunAsync(CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public async Task RunAsync_DataSourceWithoutKeepAlive_ThrowsUnsupportedBeforeConnecting()
    {
        const string expected =
            "The provider's data source has no Keep Alive, which its listening connection needs to notice a server that went away; set Keep Alive, such as 15 seconds.";
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1");
        await using var source = PgSource.FromDataSource(dataSource);
        var server = new PgServer(source, PgSettings.Default);
        var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, PgSettings.Default);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await probe.RunAsync(CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(expected, exception.Message);
    }

    #endregion

    #region CheckVersionAndRole Tests

    [Fact]
    public void CheckVersionAndRole_Version14_ThrowsUnsupported()
    {
        var exception = Assert.Throws<CoordinationException>(() =>
            PgProbe.CheckVersionAndRole(140000, inRecovery: false)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("PostgreSQL 15 or later", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckVersionAndRole_Version15_Passes()
    {
        PgProbe.CheckVersionAndRole(150000, inRecovery: false);
    }

    #endregion
}
