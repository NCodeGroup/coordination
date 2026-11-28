// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Transactions;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgServerStatementTests(PgServers servers)
{
    private static string NewSchemaName() =>
        "seam_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static async ValueTask WithSchemaAsync(
        string adminConnectionString,
        Func<string, ValueTask> test,
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

    private static PgServer NewServer(PgSource source, PgSettings? settings = null) =>
        new(source, settings ?? PgSettings.Default);

    private static ValueTask<bool> IgnoreAsync(
        PgResults results,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(true);

    private static async ValueTask<int> ReadFirstInt32Async(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(r => r.GetInt32(0), cancellationToken);
        return rows[0];
    }

    private static async ValueTask<bool> ReadTwoResultsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        await results.ReadAsync(r => r.GetInt32(0), cancellationToken);
        await results.ReadAsync(r => r.GetInt32(0), cancellationToken);
        return true;
    }

    private static async ValueTask TerminateAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = $1",
            connection
        );
        var parameter = new NpgsqlParameter { Value = applicationName };
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    #region ExecuteAsync Tests

    [PgServerFact]
    public async Task ExecuteAsync_Statement_ReturnsItsRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);
        var argument = PgArgument.Of(41);
        PgStatement[] statements = [new PgStatement("SELECT 1 + $1", argument)];

        var rows = await server.ExecuteAsync(
            statements,
            async (results, token) => await results.ReadAsync(r => r.GetInt32(0), token),
            cancellationToken
        );

        Assert.Equal([42], rows);
    }

    [PgServerFact]
    public async Task ExecuteAsync_ReadPastTheLastResult_ThrowsInvalidOperation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);
        PgStatement[] statements = [new PgStatement("SELECT 1")];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.ExecuteAsync(statements, ReadTwoResultsAsync, cancellationToken)
        );

        Assert.Equal(PgResults.NoFurtherResultMessage, exception.Message);
    }

    [PgServerFact]
    public async Task ExecuteAsync_ErrorReply_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var server = NewServer(source);
        PgStatement[] statements = [new PgStatement("SELECT 1 / 0")];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_StatementTimeout_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.Admin19);
        var statementTimeout = TimeSpan.FromMilliseconds(100);
        var settings = PgSettings.Default with { StatementTimeout = statementTimeout };
        var server = NewServer(source, settings);
        PgStatement[] statements = [new PgStatement("SELECT pg_sleep(1)")];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
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
        PgStatement[] statements = [new PgStatement("SELECT 9223372036854775807::bigint + 1")];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
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
        var statementTimeout = TimeSpan.FromSeconds(30);
        var settings = PgSettings.Default with { StatementTimeout = statementTimeout };
        var server = NewServer(source, settings);
        PgStatement[] statements = [new PgStatement("SELECT pg_sleep(5)")];

        var terminate = Task.Run(
            async () =>
            {
                await PgActivity.WaitForSleepingBackendAsync(
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
                var parameter = new NpgsqlParameter { Value = applicationName };
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
        );
        await terminate;

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [PgServerFact]
    public async Task ExecuteAsync_EveryPooledConnectionEnded_FailsOnceThenConnectsAfresh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var applicationName = "seam-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var builder = new NpgsqlConnectionStringBuilder(servers.Admin19)
        {
            ApplicationName = applicationName,
        };
        var connectionString = builder.ConnectionString;
        await using var source = PgSource.FromConnectionString(connectionString);
        var server = NewServer(source);
        PgStatement[] statements = [new PgStatement("SELECT 1")];
        var first = await source.OpenAsync(cancellationToken);
        var second = await source.OpenAsync(cancellationToken);
        await first.DisposeAsync();
        await second.DisposeAsync();
        await TerminateAsync(servers.Admin19, applicationName, cancellationToken);
        await PgActivity.WaitForNoBackendAsync(servers.Admin19, applicationName, cancellationToken);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync(statements, ReadFirstInt32Async, cancellationToken)
        );
        var row = await server.ExecuteAsync(statements, ReadFirstInt32Async, cancellationToken);

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.Equal(1, row);
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
                PgStatement[] statements = [new PgStatement($"SELECT id FROM {schema}.t")];

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
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
                var argument = PgArgument.Of(1L);
                var insert = new PgStatement($"INSERT INTO {schema}.c (p) VALUES ($1)", argument);
                PgStatement[] statements = [insert];

                await Assert.ThrowsAsync<PgKeyspaceMissingException>(async () =>
                    await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken)
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
                var argument = PgArgument.Of(1);
                var insert = new PgStatement($"INSERT INTO {schema}.t (id) VALUES ($1)", argument);
                PgStatement[] statements = [insert];

                using (
                    var scope = new TransactionScope(
                        TransactionScopeOption.Required,
                        TransactionScopeAsyncFlowOption.Enabled
                    )
                )
                {
                    await server.ExecuteAsync(statements, IgnoreAsync, cancellationToken);
                }

                var count = await PgServers.ScalarAsync(
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
        PgStatement[] statements = [new PgStatement("SELECT 1")];
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.ExecuteAsync(statements, IgnoreAsync, canceledToken)
        );
    }

    #endregion
}
