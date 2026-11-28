// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Notifications;
using NCode.Coordination.PostgreSql.Occurrences;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sessions;
using NCode.Coordination.PostgreSql.Sets;
using NCode.Coordination.PostgreSql.Subscriptions;
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
        PgSetService Sets,
        PgRecordService Records,
        PgSessionService Sessions,
        PgNotificationService Notifications
    )
    {
        public Keyspace Keyspace { get; } = new("sweep");

        public CoordinationKey KeyOf(int number) =>
            Keyspace.CreateKey("k" + number.ToString(CultureInfo.InvariantCulture));

        public Task<long> CountAsync(string table, CancellationToken cancellationToken) =>
            ScalarAsync("count(*)", table, cancellationToken);

        public async Task<long> ScalarAsync(
            string select,
            string from,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                $"SELECT {select} FROM \"{Schema}\".{from}",
                connection
            );

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return Assert.IsType<long>(scalar);
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
        CancellationToken cancellationToken,
        TimeSpan? logRetention = null
    )
    {
        var schemaName = "sweep_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var settings = PgSettings.Default with
        {
            Schema = schema,
            SweepBatchSize = 2,
            LogRetention = logRetention ?? PgSettings.Default.LogRetention,
        };

        try
        {
            await using (var source = PgSource.FromConnectionString(connectionString))
            {
                var server = new PgServer(source, settings);
                var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
                await probe.RunAsync(cancellationToken);

                var keyspaces = new PgKeyspaces(server, schema);
                var table = new PgRecordTable(server, keyspaces, settings);
                await using var hub = new PgLogHub(
                    source,
                    keyspaces,
                    settings,
                    TimeProvider.System
                );
                await test(
                    new Context(
                        adminConnectionString,
                        schemaName,
                        new PgSweeps(server, settings, table),
                        new PgOccurrenceService(server, keyspaces, schema),
                        new PgSetService(server, keyspaces, schema),
                        new PgRecordService(table),
                        new PgSessionService(table, new PgRandomTokens()),
                        new PgNotificationService(server, keyspaces, settings, hub)
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

    private Task WithSweepsAsync(
        Func<Context, Task> test,
        CancellationToken cancellationToken,
        TimeSpan? logRetention = null
    ) =>
        WithSweepsAsync(
            servers.Admin19,
            servers.PostgreSql19,
            test,
            cancellationToken,
            logRetention
        );

    #region SweepAsync Tests

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
                var afterFirst = await context.CountAsync("occurrences", cancellationToken);
                Assert.Equal(3, afterFirst);

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);
                var afterAll = await context.CountAsync("occurrences", cancellationToken);
                Assert.Equal(0, afterAll);
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

                var remaining = await context.CountAsync("occurrences", cancellationToken);

                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
                Assert.Equal(1, remaining);

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

                var occurrences = await context.CountAsync("occurrences", cancellationToken);
                var members = await context.CountAsync("set_members", cancellationToken);
                var sets = await context.CountAsync("sets", cancellationToken);

                Assert.Equal(3, occurrences);
                Assert.Equal(1, members);
                Assert.Equal(1, sets);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_ExpiredRecord_DeletesAndLogsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                var result = await context.Records.CreateAsync(
                    context.KeyOf(0),
                    new byte[] { 1 },
                    RecordLifetime.ForTimeToLive(new TimeToLive(Expired)),
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                var before = await context.CountAsync("records", cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var records = await context.CountAsync("records", cancellationToken);
                var changes = await context.CountAsync("changes", cancellationToken);
                var deletions = await context.CountAsync(
                    "change_items WHERE kind = 2",
                    cancellationToken
                );
                var revision = await context.ScalarAsync(
                    "revision",
                    "keyspaces",
                    cancellationToken
                );

                Assert.Equal(1, before);
                Assert.Equal(0, records);
                Assert.Equal(2, changes);
                Assert.Equal(1, deletions);
                Assert.Equal(3, revision);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_EntriesPastRetention_RaisesLogRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                for (var number = 0; number < 2; number++)
                {
                    await context.Records.CreateAsync(
                        context.KeyOf(number),
                        new byte[] { 1 },
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                }

                var before = await context.CountAsync("changes", cancellationToken);
                Assert.Equal(2, before);
                await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var changes = await context.CountAsync("changes", cancellationToken);
                var items = await context.CountAsync("change_items", cancellationToken);
                var logRemoved = await context.ScalarAsync(
                    "log_removed",
                    "keyspaces",
                    cancellationToken
                );

                Assert.Equal(0, changes);
                Assert.Equal(0, items);
                Assert.Equal(3, logRemoved);
            },
            cancellationToken,
            TimeSpan.FromSeconds(1)
        );
    }

    [PgServerFact]
    public async Task SweepAsync_MessagesPastRetention_RaisesRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                for (var number = 0; number < 3; number++)
                {
                    await context.Notifications.PublishAsync(
                        context.KeyOf(0),
                        new byte[] { 1 },
                        null,
                        cancellationToken
                    );
                }

                var before = await context.CountAsync("messages", cancellationToken);
                Assert.Equal(3, before);
                await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var afterFirst = await context.CountAsync("messages", cancellationToken);
                var removedFirst = await context.ScalarAsync(
                    "removed",
                    "topics",
                    cancellationToken
                );
                Assert.Equal(1, afterFirst);
                Assert.Equal(2, removedFirst);

                await context.Sweeps.SweepAsync(cancellationToken);

                var afterSecond = await context.CountAsync("messages", cancellationToken);
                var removedSecond = await context.ScalarAsync(
                    "removed",
                    "topics",
                    cancellationToken
                );
                Assert.Equal(0, afterSecond);
                Assert.Equal(3, removedSecond);
            },
            cancellationToken,
            TimeSpan.FromSeconds(1)
        );
    }

    [PgServerFact]
    public async Task SweepAsync_ExpiredSession_EndsItAsOneChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.Sessions.OpenAsync(
                    context.Keyspace,
                    new TimeToLive(Expired),
                    null,
                    cancellationToken
                );
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                var before = await context.CountAsync("sessions", cancellationToken);
                Assert.Equal(1, before);

                await context.Sweeps.SweepAsync(cancellationToken);

                var sessions = await context.CountAsync("sessions", cancellationToken);
                var changes = await context.CountAsync("changes", cancellationToken);
                var ended = await context.CountAsync(
                    "changes WHERE ended_session IS NOT NULL",
                    cancellationToken
                );
                var revision = await context.ScalarAsync(
                    "revision",
                    "keyspaces",
                    cancellationToken
                );

                Assert.Equal(0, sessions);
                Assert.Equal(1, changes);
                Assert.Equal(1, ended);
                Assert.Equal(2, revision);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_BoundRecordOfEndedSession_KeepsItWhileTheEntryIsLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    new TimeToLive(TimeSpan.FromMilliseconds(300)),
                    null,
                    cancellationToken
                );
                var result = await context.Records.CreateAsync(
                    context.KeyOf(0),
                    new byte[] { 1 },
                    RecordLifetime.ForSession(grant.Id),
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                var sessions = await context.CountAsync("sessions", cancellationToken);
                var records = await context.CountAsync("records", cancellationToken);

                Assert.Equal(0, sessions);
                Assert.Equal(1, records);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_BoundRecordAfterTheEntryLeftTheLog_RemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    new TimeToLive(TimeSpan.FromMilliseconds(300)),
                    null,
                    cancellationToken
                );
                var result = await context.Records.CreateAsync(
                    context.KeyOf(0),
                    new byte[] { 1 },
                    RecordLifetime.ForSession(grant.Id),
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                var kept = await context.CountAsync("records", cancellationToken);
                Assert.Equal(1, kept);

                await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                var changes = await context.CountAsync("changes", cancellationToken);
                var records = await context.CountAsync("records", cancellationToken);

                Assert.Equal(0, changes);
                Assert.Equal(0, records);
            },
            cancellationToken,
            TimeSpan.FromSeconds(1)
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

                var members = await context.CountAsync("set_members", cancellationToken);
                var sets = await context.CountAsync("sets", cancellationToken);

                Assert.Equal(0, members);
                Assert.Equal(0, sets);
            },
            cancellationToken
        );
    }

    #endregion
}
