// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Transactions;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgServerStatementTests(PgServers servers)
{
    private static string NewSchemaName() =>
        "seam_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static async Task WithSchemaAsync(
        string adminConnectionString,
        Func<string, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schema = NewSchemaName();
        await PgServers.ExecuteAsAdminAsync(
            adminConnectionString,
            $"CREATE SCHEMA {schema}",
            cancellationToken
        );

        try
        {
            await test(schema);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                adminConnectionString,
                $"DROP SCHEMA IF EXISTS {schema} CASCADE",
                CancellationToken.None
            );
        }
    }

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

    private static async Task WaitForSleepingBackendAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = $1 "
                    + "AND state = 'active' AND query LIKE '%pg_sleep(5)%'",
                connection
            );
            command.Parameters.Add(new NpgsqlParameter { Value = applicationName });
            var count = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            );
            if (count > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        Assert.Fail("No backend of the application ran pg_sleep(5) within 15 seconds.");
    }

    private static PgServer NewServer(PgSource source, PgSettings? settings = null) =>
        new(source, settings ?? PgSettings.Default);

    private static ValueTask<bool> Ignore(PgResults results, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    [PgServerFact]
    public async Task ExecuteAsync_Statement_ReturnsItsRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);

        var rows = await server.ExecuteAsync(
            [new PgStatement("SELECT 1 + $1", PgArgument.Of(41))],
            async (results, token) => await results.ReadAsync(r => r.GetInt32(0), token),
            cancellationToken
        );

        Assert.Equal([42], rows);
    }

    [PgServerFact]
    public async Task ExecuteAsync_ErrorReply_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync([new PgStatement("SELECT 1 / 0")], Ignore, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_StatementTimeout_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(
            source,
            PgSettings.Default with
            {
                StatementTimeout = TimeSpan.FromMilliseconds(100),
            }
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(
                [new PgStatement("SELECT pg_sleep(1)")],
                Ignore,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_BigintOverflow_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(
                [new PgStatement("SELECT 9223372036854775807::bigint + 1")],
                Ignore,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_TerminatedMidStatement_ThrowsOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var applicationName = "seam-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var connectionString = new NpgsqlConnectionStringBuilder(servers.Admin19)
        {
            ApplicationName = applicationName,
        }.ConnectionString;
        await using var source = PgSource.FromConnectionString(connectionString);
        var server = NewServer(
            source,
            PgSettings.Default with
            {
                StatementTimeout = TimeSpan.FromSeconds(30),
            }
        );

        var terminate = Task.Run(
            async () =>
            {
                await WaitForSleepingBackendAsync(
                    servers.Admin19,
                    applicationName,
                    cancellationToken
                );

                await using var connection = new NpgsqlConnection(servers.Admin19);
                await connection.OpenAsync(cancellationToken);

                await using var command = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = $1",
                    connection
                );
                command.Parameters.Add(new NpgsqlParameter { Value = applicationName });
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(
                [new PgStatement("SELECT pg_sleep(5)")],
                Ignore,
                cancellationToken
            )
        );
        await terminate;

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_RoleWithoutPrivilege_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(
            servers.Admin19,
            async schema =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"CREATE TABLE {schema}.t (id int PRIMARY KEY)",
                    cancellationToken
                );
                await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
                var server = NewServer(source);

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.ExecuteAsync(
                        [new PgStatement($"SELECT id FROM {schema}.t")],
                        Ignore,
                        cancellationToken
                    )
                );

                Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
                Assert.False(exception.OutcomeUnknown);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ExecuteAsync_ForeignKeyViolation_ThrowsKeyspaceMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(
            servers.Admin19,
            async schema =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"CREATE TABLE {schema}.p (id bigint PRIMARY KEY); "
                        + $"CREATE TABLE {schema}.c (p bigint REFERENCES {schema}.p)",
                    cancellationToken
                );
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = NewServer(source);

                await Assert.ThrowsAsync<PgKeyspaceMissingException>(async () =>
                    await server.ExecuteAsync(
                        [
                            new PgStatement(
                                $"INSERT INTO {schema}.c (p) VALUES ($1)",
                                PgArgument.Of(1L)
                            ),
                        ],
                        Ignore,
                        cancellationToken
                    )
                );
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ExecuteAsync_InsideAmbientTransaction_IsNotEnlisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(
            servers.Admin19,
            async schema =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"CREATE TABLE {schema}.t (id int PRIMARY KEY)",
                    cancellationToken
                );
                await using var dataSource = NpgsqlDataSource.Create(servers.Admin19);
                await using var source = PgSource.FromDataSource(dataSource);
                var server = NewServer(source);

                using (
                    var scope = new TransactionScope(
                        TransactionScopeOption.Required,
                        TransactionScopeAsyncFlowOption.Enabled
                    )
                )
                {
                    await server.ExecuteAsync(
                        [
                            new PgStatement(
                                $"INSERT INTO {schema}.t (id) VALUES ($1)",
                                PgArgument.Of(1)
                            ),
                        ],
                        Ignore,
                        cancellationToken
                    );
                }

                var count = await ScalarAsync(
                    servers.Admin19,
                    $"SELECT count(*) FROM {schema}.t",
                    cancellationToken
                );
                Assert.Equal(1L, count);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ExecuteAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.ExecuteAsync(
                [new PgStatement("SELECT 1")],
                Ignore,
                new CancellationToken(canceled: true)
            )
        );
    }
}
