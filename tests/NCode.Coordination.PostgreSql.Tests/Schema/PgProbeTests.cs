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
        Assert.Contains("Multiplexing", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("Keep Alive", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
