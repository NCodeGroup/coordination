// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

public sealed class PgRestartableServerTests
{
    private static async ValueTask<string> ReadAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT value FROM kept", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Assert.IsType<string>(value);
    }

    #region StartAgainAsync Tests

    [PgServerFact]
    public async Task StartAgainAsync_AfterStop_KeepsTheEndpointAndTheData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        var endpoint = server.ConnectionString;
        await PgServers.ExecuteAsAdminAsync(
            endpoint,
            "CREATE TABLE kept (value text); INSERT INTO kept VALUES ('kept')",
            cancellationToken
        );

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

        Assert.Equal(endpoint, server.ConnectionString);
        var value = await ReadAsync(server.ConnectionString, cancellationToken);
        Assert.Equal("kept", value);
    }

    #endregion
}
