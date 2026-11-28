// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgServerTransactionTests(PgServers servers)
{
    private static string NewName(string prefix) =>
        prefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static async Task WithTableAsync(
        string adminConnectionString,
        Func<string, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schema = NewName("seam_");
        await PgServers.ExecuteAsAdminAsync(
            adminConnectionString,
            $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.t (id int PRIMARY KEY)",
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

    private static async Task<long> CountAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.t", connection);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private static async Task TerminateAsync(
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
        command.Parameters.Add(new NpgsqlParameter { Value = applicationName });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WaitForBackendsAsync(
        string adminConnectionString,
        string applicationName,
        string filter,
        Func<long, bool> isDone,
        string failure,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = $1" + filter,
                connection
            );
            command.Parameters.Add(new NpgsqlParameter { Value = applicationName });
            var count = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            );
            if (isDone(count))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        Assert.Fail(failure);
    }

    private static Task WaitForNoBackendAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    ) =>
        WaitForBackendsAsync(
            adminConnectionString,
            applicationName,
            string.Empty,
            count => count == 0,
            "A backend of the application was still running 15 seconds after it was ended.",
            cancellationToken
        );

    private static Task WaitForSleepingBackendAsync(
        string adminConnectionString,
        string applicationName,
        CancellationToken cancellationToken
    ) =>
        WaitForBackendsAsync(
            adminConnectionString,
            applicationName,
            " AND state = 'active' AND query LIKE '%pg_sleep(5)%'",
            count => count > 0,
            "No backend of the application ran pg_sleep(5) within 15 seconds.",
            cancellationToken
        );

    private static string WithApplicationName(string connectionString, string applicationName) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;

    private static PgStatement Insert(string schema) =>
        new($"INSERT INTO {schema}.t (id) VALUES ($1)", PgArgument.Of(1));

    private static ValueTask<bool> Ignore(PgResults results, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    private static async ValueTask<long> ReadCount(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(r => r.GetInt64(0), cancellationToken);
        return rows[0];
    }

    [PgServerFact]
    public async Task TransactAsync_Commit_AppliesTheWrites()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(source, PgSettings.Default);

                await server.TransactAsync(
                    PgIsolation.ReadCommitted,
                    async (transaction, token) =>
                    {
                        await transaction.FirstAsync([new PgStatement("SELECT 1")], Ignore, token);
                        await transaction.CommitAsync([Insert(schema)], token);
                        return true;
                    },
                    cancellationToken
                );

                Assert.Equal(1L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_Rollback_AppliesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(source, PgSettings.Default);

                await server.TransactAsync(
                    PgIsolation.ReadCommitted,
                    async (transaction, token) =>
                    {
                        await transaction.FirstAsync([new PgStatement("SELECT 1")], Ignore, token);
                        await transaction.NextAsync([Insert(schema)], Ignore, token);
                        await transaction.RollbackAsync(token);
                        return true;
                    },
                    cancellationToken
                );

                Assert.Equal(0L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_ErrorInTheCommitBatch_ThrowsNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(source, PgSettings.Default);

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.TransactAsync(
                        PgIsolation.ReadCommitted,
                        async (transaction, token) =>
                        {
                            await transaction.FirstAsync(
                                [new PgStatement("SELECT 1")],
                                Ignore,
                                token
                            );
                            await transaction.CommitAsync(
                                [Insert(schema), new PgStatement("SELECT 1 / 0")],
                                token
                            );
                            return true;
                        },
                        cancellationToken
                    )
                );

                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                Assert.False(exception.OutcomeUnknown);
                Assert.Equal(0L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_IdleBetweenBatchesPastTimeout_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(
                    source,
                    PgSettings.Default with
                    {
                        IdleInTransactionTimeout = TimeSpan.FromMilliseconds(200),
                    }
                )
                {
                    AfterFirstBatch = async token =>
                        await Task.Delay(TimeSpan.FromMilliseconds(600), token),
                };

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.TransactAsync(
                        PgIsolation.ReadCommitted,
                        async (transaction, token) =>
                        {
                            await transaction.FirstAsync(
                                [new PgStatement("SELECT 1")],
                                Ignore,
                                token
                            );
                            await transaction.CommitAsync([Insert(schema)], token);
                            return true;
                        },
                        cancellationToken
                    )
                );

                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                Assert.False(exception.OutcomeUnknown);
                Assert.Equal(0L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_TerminatedBeforeCommit_ThrowsUnavailableAndAppliesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                var applicationName = NewName("seam-");
                await using var source = PgSource.FromConnectionString(
                    WithApplicationName(servers.Admin19, applicationName)
                );
                var server = new PgServer(source, PgSettings.Default)
                {
                    AfterFirstBatch = async token =>
                    {
                        await TerminateAsync(servers.Admin19, applicationName, token);
                        await WaitForNoBackendAsync(servers.Admin19, applicationName, token);
                    },
                };

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.TransactAsync(
                        PgIsolation.ReadCommitted,
                        async (transaction, token) =>
                        {
                            await transaction.FirstAsync(
                                [new PgStatement("SELECT 1")],
                                Ignore,
                                token
                            );
                            await transaction.CommitAsync([Insert(schema)], token);
                            return true;
                        },
                        cancellationToken
                    )
                );

                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                Assert.True(exception.OutcomeUnknown);
                Assert.Equal(0L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_TerminatedAfterCommitSent_ThrowsOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                var applicationName = NewName("seam-");
                await using var source = PgSource.FromConnectionString(
                    WithApplicationName(servers.Admin19, applicationName)
                );
                var server = new PgServer(
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
                        await TerminateAsync(servers.Admin19, applicationName, cancellationToken);
                    },
                    cancellationToken
                );

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.TransactAsync(
                        PgIsolation.ReadCommitted,
                        async (transaction, token) =>
                        {
                            await transaction.FirstAsync(
                                [new PgStatement("SELECT 1")],
                                Ignore,
                                token
                            );
                            await transaction.CommitAsync(
                                [Insert(schema), new PgStatement("SELECT pg_sleep(5)")],
                                token
                            );
                            return true;
                        },
                        cancellationToken
                    )
                );
                await terminate;

                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                Assert.True(exception.OutcomeUnknown);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_LockTimeout_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"INSERT INTO {schema}.t (id) VALUES (1)",
                    cancellationToken
                );

                await using var holder = new NpgsqlConnection(servers.Admin19);
                await holder.OpenAsync(cancellationToken);
                await using var holderTransaction = await holder.BeginTransactionAsync(
                    cancellationToken
                );
                await using (
                    var lockCommand = new NpgsqlCommand(
                        $"SELECT id FROM {schema}.t WHERE id = 1 FOR UPDATE",
                        holder,
                        holderTransaction
                    )
                )
                {
                    await lockCommand.ExecuteScalarAsync(cancellationToken);
                }

                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(
                    source,
                    PgSettings.Default with
                    {
                        LockTimeout = TimeSpan.FromMilliseconds(100),
                    }
                );

                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await server.TransactAsync(
                        PgIsolation.ReadCommitted,
                        async (transaction, token) =>
                        {
                            await transaction.FirstAsync(
                                [
                                    new PgStatement(
                                        $"SELECT id FROM {schema}.t WHERE id = 1 FOR UPDATE"
                                    ),
                                ],
                                Ignore,
                                token
                            );
                            await transaction.CommitAsync([Insert(schema)], token);
                            return true;
                        },
                        cancellationToken
                    )
                );

                Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
                Assert.False(exception.OutcomeUnknown);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TransactAsync_RepeatableRead_ReadsOneSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithTableAsync(
            servers.Admin19,
            async schema =>
            {
                await using var source = PgSource.FromConnectionString(servers.Admin19);
                var server = new PgServer(source, PgSettings.Default);
                var count = new PgStatement($"SELECT count(*) FROM {schema}.t");

                var (first, second) = await server.TransactAsync(
                    PgIsolation.RepeatableReadReadOnly,
                    async (transaction, token) =>
                    {
                        var before = await transaction.FirstAsync([count], ReadCount, token);

                        await PgServers.ExecuteAsAdminAsync(
                            servers.Admin19,
                            $"INSERT INTO {schema}.t (id) VALUES (1)",
                            token
                        );

                        var after = await transaction.NextAsync([count], ReadCount, token);
                        return (before, after);
                    },
                    cancellationToken
                );

                Assert.Equal(0L, first);
                Assert.Equal(first, second);
                Assert.Equal(1L, await CountAsync(servers.Admin19, schema, cancellationToken));
            },
            cancellationToken
        );
    }
}
