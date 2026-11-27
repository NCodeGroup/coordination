// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Occurrences;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sets;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Sweep.Tests;

public sealed class PgSweepsTests(PgServers servers)
{
    private static readonly TimeSpan Expired = TimeSpan.FromTicks(10);

    private static readonly TimeSpan Live = TimeSpan.FromMinutes(1);

    private sealed record Context(
        string AdminConnectionString,
        string Schema,
        PgSweeps Sweeps,
        PgOccurrenceService Occurrences,
        PgSetService Sets
    )
    {
        public Keyspace Keyspace { get; } = new("sweep");

        public CoordinationKey KeyOf(int number) =>
            Keyspace.CreateKey("k" + number.ToString(CultureInfo.InvariantCulture));

        public async Task<long> CountAsync(string table, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                $"SELECT count(*) FROM \"{Schema}\".{table}",
                connection
            );

            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        public async Task RecordAsync(
            int count,
            TimeSpan timeToLive,
            CancellationToken cancellationToken
        )
        {
            for (var number = 0; number < count; number++)
            {
                var status = await Occurrences.TryRecordAsync(
                    KeyOf(number),
                    new TimeToLive(timeToLive),
                    null,
                    cancellationToken
                );
                Assert.Equal(OccurrenceStatus.Recorded, status);
            }
        }
    }

    private static async Task WithSweepsAsync(
        string adminConnectionString,
        string connectionString,
        Func<Context, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "sweep_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var settings = PgSettings.Default with { Schema = schema, SweepBatchSize = 2 };

        try
        {
            await using (var source = PgSource.FromConnectionString(connectionString))
            {
                var server = new PgServer(source, settings);
                var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
                await probe.RunAsync(cancellationToken);

                var keyspaces = new PgKeyspaces(server, schema);
                await test(
                    new Context(
                        adminConnectionString,
                        schemaName,
                        new PgSweeps(server, settings),
                        new PgOccurrenceService(server, keyspaces, schema),
                        new PgSetService(server, keyspaces, schema)
                    )
                );
            }
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                adminConnectionString,
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE",
                CancellationToken.None
            );
        }
    }

    private Task WithSweepsAsync(Func<Context, Task> test, CancellationToken cancellationToken) =>
        WithSweepsAsync(servers.Admin19, servers.PostgreSql19, test, cancellationToken);

    [PgServerFact]
    public async Task SweepAsync_FiveExpiredOccurrences_RemovesTwoPerPass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.RecordAsync(5, Expired, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                Assert.Equal(3, await context.CountAsync("occurrences", cancellationToken));

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);
                Assert.Equal(0, await context.CountAsync("occurrences", cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_LockedRow_SkipsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.RecordAsync(3, Expired, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                await using var holder = new NpgsqlConnection(context.AdminConnectionString);
                await holder.OpenAsync(cancellationToken);
                await using var transaction = await holder.BeginTransactionAsync(cancellationToken);
                await using var hold = new NpgsqlCommand(
                    $"SELECT 1 FROM \"{context.Schema}\".occurrences WHERE key = $1 FOR UPDATE",
                    holder,
                    transaction
                )
                {
                    Parameters =
                    {
                        new NpgsqlParameter { Value = PgKeys.KeyText(context.KeyOf(0)) },
                    },
                };
                await hold.ExecuteNonQueryAsync(cancellationToken);

                var stopwatch = Stopwatch.StartNew();
                await context
                    .Sweeps.SweepAsync(cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                stopwatch.Stop();

                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
                Assert.Equal(1, await context.CountAsync("occurrences", cancellationToken));

                await transaction.RollbackAsync(cancellationToken);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_LiveRows_KeepsThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.RecordAsync(3, Live, cancellationToken);
                await context.Sets.AddAsync(
                    context.KeyOf(100),
                    KeySegment.FromString("member"),
                    new TimeToLive(Live),
                    null,
                    cancellationToken
                );

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                Assert.Equal(3, await context.CountAsync("occurrences", cancellationToken));
                Assert.Equal(1, await context.CountAsync("set_members", cancellationToken));
                Assert.Equal(1, await context.CountAsync("sets", cancellationToken));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_SetWithoutMembers_RemovesItsRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.Sets.AddAsync(
                    context.KeyOf(0),
                    KeySegment.FromString("member"),
                    new TimeToLive(Expired),
                    null,
                    cancellationToken
                );
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                Assert.Equal(0, await context.CountAsync("set_members", cancellationToken));
                Assert.Equal(0, await context.CountAsync("sets", cancellationToken));
            },
            cancellationToken
        );
    }
}
