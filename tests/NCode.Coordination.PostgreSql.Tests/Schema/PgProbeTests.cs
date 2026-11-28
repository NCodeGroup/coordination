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
        Assert.Equal(PgProbe.MultiplexingMessage, exception.Message);
    }

    [Fact]
    public async Task RunAsync_DataSourceWithoutKeepAlive_ThrowsUnsupportedBeforeConnecting()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1");
        await using var source = PgSource.FromDataSource(dataSource);
        var server = new PgServer(source, PgSettings.Default);
        var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, PgSettings.Default);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await probe.RunAsync(CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(PgProbe.NoKeepAliveMessage, exception.Message);
    }

    #endregion

    #region CheckVersionAndRole Tests

    [Fact]
    public void CheckVersionAndRole_Version14_ThrowsUnsupported()
    {
        var expected = PgProbe.TooOldMessage(140000);
        var exception = Assert.Throws<CoordinationException>(() =>
            PgProbe.CheckVersionAndRole(140000, inRecovery: false)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public void CheckVersionAndRole_Version15_Passes()
    {
        PgProbe.CheckVersionAndRole(150000, inRecovery: false);
    }

    #endregion

    #region CheckSynchronousStandby Tests

    [Theory]
    [InlineData("on")]
    [InlineData("remote_apply")]
    public void CheckSynchronousStandby_StandbyNamedAndCommitWaitsForIt_Passes(string commit)
    {
        PgProbe.CheckSynchronousStandby(PostgreSqlProfiles.Synchronous, commit, "standby1");
    }

    [Theory]
    [InlineData("remote_write", "standby1")]
    [InlineData("local", "standby1")]
    [InlineData("on", "")]
    public void CheckSynchronousStandby_CommitOrStandbyMissing_ThrowsUnsupported(
        string commit,
        string names
    )
    {
        var exception = Assert.Throws<CoordinationException>(() =>
            PgProbe.CheckSynchronousStandby(PostgreSqlProfiles.Synchronous, commit, names)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.Contains("synchronous_standby_names", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
