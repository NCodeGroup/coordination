// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

public sealed class PgServersTests(PgServers servers)
{
    private static async Task<object?> ScalarAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<string> ShowAsync(
        string connectionString,
        string setting,
        CancellationToken cancellationToken
    )
    {
        var value = await ScalarAsync(connectionString, $"SHOW {setting}", cancellationToken);

        return Assert.IsType<string>(value);
    }

    private static async Task<int> MajorVersionAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var text = await ShowAsync(connectionString, "server_version_num", cancellationToken);
        var number = int.Parse(text, CultureInfo.InvariantCulture);

        return number / 10000;
    }

    #region PostgreSql19 Tests

    [PgServerFact]
    public async Task PostgreSql19_Started_ReportsVersion19()
    {
        var major = await MajorVersionAsync(
            servers.PostgreSql19,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(19, major);
    }

    [PgServerFact]
    public async Task PostgreSql19_Started_RunsWithFsync()
    {
        var value = await ShowAsync(
            servers.Admin19,
            "fsync",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("on", value);
    }

    #endregion

    #region PostgreSql15 Tests

    [PgServerFact]
    public async Task PostgreSql15_Started_ReportsVersion15()
    {
        var major = await MajorVersionAsync(
            servers.PostgreSql15,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(15, major);
    }

    #endregion

    #region Unsafe19 Tests

    [PgServerFact]
    public async Task Unsafe19_Started_RunsWithoutFsync()
    {
        var value = await ShowAsync(
            servers.Unsafe19,
            "fsync",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("off", value);
    }

    #endregion

    #region Coordinator Tests

    [PgServerFact]
    public async Task Coordinator_Always_IsNotASuperuser()
    {
        var value = await ScalarAsync(
            servers.PostgreSql19,
            "SELECT rolsuper FROM pg_roles WHERE rolname = current_user",
            TestContext.Current.CancellationToken
        );

        var isSuperuser = Assert.IsType<bool>(value);
        Assert.False(isSuperuser);
    }

    #endregion
}
