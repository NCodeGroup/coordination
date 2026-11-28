// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Logging;
using NCode.Coordination.PostgreSql.Notifications;
using NCode.Coordination.PostgreSql.Occurrences;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sessions;
using NCode.Coordination.PostgreSql.Sets;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Tests;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Sweep.Tests;

public sealed class PgSweepsTests(PgServers servers)
{
    private static TimeSpan Live { get; } = TimeSpan.FromMinutes(1);

    private static TimeSpan LockedSweepLimit { get; } = TimeSpan.FromSeconds(3);

    private static TimeToLive LiveTimeToLive { get; } = new(Live);

    private static RecordLifetime LiveLifetime { get; } =
        RecordLifetime.ForTimeToLive(LiveTimeToLive);

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

        public CoordinationKey KeyOf(int number)
        {
            var name = "k" + number.ToString(CultureInfo.InvariantCulture);
            return Keyspace.CreateKey(name);
        }

        public Task ExpireAsync(string table, CancellationToken cancellationToken) =>
            PgServers
                .ExpireRowsAsync(AdminConnectionString, Schema, table, cancellationToken)
                .AsTask();

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
                var key = KeyOf(number);
                var lifetime = new TimeToLive(timeToLive);
                var status = await Occurrences.TryRecordAsync(
                    key,
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.Equal(OccurrenceStatus.Recorded, status);
            }
        }
    }

    private static string[] ValuesOf(IReadOnlyList<FakeLogRecord> records, int eventId, string name)
    {
        var matching = records.Where(record => record.Id.Id == eventId);
        return [.. matching.Select(record => record.GetStructuredStateValue(name) ?? string.Empty)];
    }

    private static ValueTask ThrowUnexpectedAsync(CancellationToken cancellationToken)
    {
        var failure = new InvalidOperationException("broken");
        return ValueTask.FromException(failure);
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
            await using var source = PgSource.FromConnectionString(connectionString);
            var server = new PgServer(source, settings);
            var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
            await probe.RunAsync(cancellationToken);

            var keyspaceTokens = new PgRandomTokens();
            var keyspaces = new PgKeyspaces(server, schema, keyspaceTokens);
            var table = new PgRecordTable(server, keyspaces, settings);
            await using var hub = new PgLogHub(
                source,
                keyspaces,
                settings,
                TimeProvider.System,
                NullLoggerFactory.Instance
            );
            var polling = new PgPolling(TimeProvider.System);
            var sweeps = new PgSweeps(server, settings, table, NullLogger<PgSweeps>.Instance);
            var occurrences = new PgOccurrenceService(server, keyspaces, schema);
            var sets = new PgSetService(server, keyspaces, schema);
            var records = new PgRecordService(table);
            var sessionTokens = new PgRandomTokens();
            var sessions = new PgSessionService(table, sessionTokens, polling);
            var notifications = new PgNotificationService(server, keyspaces, settings, hub);
            var context = new Context(
                adminConnectionString,
                schemaName,
                sweeps,
                occurrences,
                sets,
                records,
                sessions,
                notifications
            );
            await test(context);
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

    #region SweepAsync Tests

    [PgServerFact]
    public async Task SweepAsync_FiveExpiredOccurrences_RemovesThemAllInBatchesOfTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                await context.RecordAsync(5, Live, cancellationToken);
                await context.ExpireAsync("occurrences", cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var remaining = await context.CountAsync("occurrences", cancellationToken);
                Assert.Equal(0, remaining);
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
                await context.RecordAsync(3, Live, cancellationToken);
                await context.ExpireAsync("occurrences", cancellationToken);

                var key = context.KeyOf(0);
                var keyText = PgKeys.KeyText(key);
                await using var holder = new NpgsqlConnection(context.AdminConnectionString);
                await holder.OpenAsync(cancellationToken);
                await using var transaction = await holder.BeginTransactionAsync(cancellationToken);
                await using var hold = new NpgsqlCommand(
                    $"SELECT 1 FROM \"{context.Schema}\".occurrences WHERE key = $1 FOR UPDATE",
                    holder,
                    transaction
                )
                {
                    Parameters = { new NpgsqlParameter { Value = keyText } },
                };
                await hold.ExecuteNonQueryAsync(cancellationToken);

                await context
                    .Sweeps.SweepAsync(cancellationToken)
                    .AsTask()
                    .WaitAsync(LockedSweepLimit, cancellationToken);

                var remaining = await context.CountAsync("occurrences", cancellationToken);

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
                var member = KeySegment.FromString("member");
                var setKey = context.KeyOf(100);
                await context.Sets.AddAsync(
                    setKey,
                    member,
                    LiveTimeToLive,
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
                var key = context.KeyOf(0);
                var result = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    LiveLifetime,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                var before = await context.CountAsync("records", cancellationToken);
                await context.ExpireAsync("records", cancellationToken);

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
    public async Task SweepAsync_FiveExpiredRecords_LogsThemInEntriesOfTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                for (var number = 0; number < 5; number++)
                {
                    var key = context.KeyOf(number);
                    var result = await context.Records.CreateAsync(
                        key,
                        new byte[] { 1 },
                        LiveLifetime,
                        null,
                        cancellationToken
                    );
                    Assert.True(result.IsWritten);
                }

                await context.ExpireAsync("records", cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var records = await context.CountAsync("records", cancellationToken);
                var deletions = await context.CountAsync(
                    "change_items WHERE kind = 2",
                    cancellationToken
                );
                var changes = await context.CountAsync("changes", cancellationToken);

                Assert.Equal(0, records);
                Assert.Equal(5, deletions);
                Assert.Equal(8, changes);
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
                    var key = context.KeyOf(number);
                    await context.Records.CreateAsync(
                        key,
                        new byte[] { 1 },
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                }

                var before = await context.CountAsync("changes", cancellationToken);
                Assert.Equal(2, before);
                await context.ExpireAsync("changes", cancellationToken);

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
            cancellationToken
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
                    var key = context.KeyOf(0);
                    await context.Notifications.PublishAsync(
                        key,
                        new byte[] { 1 },
                        null,
                        cancellationToken
                    );
                }

                var before = await context.CountAsync("messages", cancellationToken);
                Assert.Equal(3, before);
                await context.ExpireAsync("messages", cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);

                var messages = await context.CountAsync("messages", cancellationToken);
                var removed = await context.ScalarAsync("removed", "topics", cancellationToken);

                Assert.Equal(0, messages);
                Assert.Equal(3, removed);
            },
            cancellationToken
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
                    LiveTimeToLive,
                    null,
                    cancellationToken
                );
                await context.ExpireAsync("sessions", cancellationToken);
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
                    LiveTimeToLive,
                    null,
                    cancellationToken
                );
                var key = context.KeyOf(0);
                var lifetime = RecordLifetime.ForSession(grant.Id);
                var result = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                await context.ExpireAsync("sessions", cancellationToken);

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
                    LiveTimeToLive,
                    null,
                    cancellationToken
                );
                var key = context.KeyOf(0);
                var lifetime = RecordLifetime.ForSession(grant.Id);
                var result = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                await context.ExpireAsync("sessions", cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                var kept = await context.CountAsync("records", cancellationToken);
                Assert.Equal(1, kept);

                await context.ExpireAsync("changes", cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                var changes = await context.CountAsync("changes", cancellationToken);
                var records = await context.CountAsync("records", cancellationToken);

                Assert.Equal(0, changes);
                Assert.Equal(0, records);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task SweepAsync_EndedSessionAfterItsEntryLeftTheLog_ForgetsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    LiveTimeToLive,
                    null,
                    cancellationToken
                );
                var key = context.KeyOf(0);
                var lifetime = RecordLifetime.ForSession(grant.Id);
                var result = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);
                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);
                var queued = await context.CountAsync("ended_sessions", cancellationToken);
                Assert.Equal(1, queued);

                await context.ExpireAsync("changes", cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                var records = await context.CountAsync("records", cancellationToken);
                var ended = await context.CountAsync("ended_sessions", cancellationToken);

                Assert.Equal(0, records);
                Assert.Equal(0, ended);
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
                var key = context.KeyOf(0);
                var member = KeySegment.FromString("member");
                await context.Sets.AddAsync(key, member, LiveTimeToLive, null, cancellationToken);
                await context.ExpireAsync("set_members", cancellationToken);
                await context.ExpireAsync("sets", cancellationToken);

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

    [PgServerFact]
    public async Task SweepAsync_SetEmptiedBeforeItsDeadline_KeepsItsRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweepsAsync(
            async context =>
            {
                var key = context.KeyOf(0);
                var member = KeySegment.FromString("member");
                await context.Sets.AddAsync(key, member, LiveTimeToLive, null, cancellationToken);
                await context.Sets.RemoveAsync(key, member, null, cancellationToken);

                await context.Sweeps.SweepAsync(cancellationToken);
                await context.Sweeps.SweepAsync(cancellationToken);

                var sets = await context.CountAsync("sets", cancellationToken);

                Assert.Equal(1, sets);
            },
            cancellationToken
        );
    }

    [Fact]
    public async Task SweepAsync_ServerUnreachable_LogsEachPassAndStepOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var offline = new PgOfflineServer();
        var logger = new FakeLogger<PgSweeps>();
        var sweeps = new PgSweeps(offline.Server, offline.Settings, offline.Table, logger);
        string[] expectedPasses =
        [
            "occurrences",
            "counters",
            "set members",
            "sets",
            "bound records",
            "bound members",
            "bound grants",
            "ended sessions",
            "grants",
            "claims",
            "change log",
            "messages",
        ];
        string[] expectedSteps = ["records", "sessions"];

        await sweeps.SweepAsync(cancellationToken);

        var records = logger.Collector.GetSnapshot();
        var passes = ValuesOf(records, EventIds.SweepPassFailed, "Pass");
        var steps = ValuesOf(records, EventIds.SweepStepFailed, "Step");
        Assert.Equal(expectedPasses, passes);
        Assert.Equal(expectedSteps, steps);
    }

    #endregion

    #region RunPassAsync Tests

    [PgServerFact]
    public async Task RunPassAsync_PassFailsUnexpectedly_LogsSweepPassFaultedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var tokens = new PgRandomTokens();
        var keyspaces = new PgKeyspaces(server, settings.Schema, tokens);
        var table = new PgRecordTable(server, keyspaces, settings);
        var logger = new FakeLogger<PgSweeps>();
        var sweeps = new PgSweeps(server, settings, table, logger);
        const string name = "broken pass";
        var pass = new PgSweepPass(name, "SELECT 'broken'::text WHERE $1::int > 0");

        await sweeps.RunPassAsync(pass, cancellationToken);

        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records);
        var logged = record.GetStructuredStateValue("Pass");
        Assert.Equal(EventIds.SweepPassFaulted, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(name, logged);
    }

    #endregion

    #region IgnoreFailureAsync Tests

    [Fact]
    public async Task IgnoreFailureAsync_StepThrowsUnexpectedly_LogsSweepStepFaultedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var offline = new PgOfflineServer();
        var logger = new FakeLogger<PgSweeps>();
        var sweeps = new PgSweeps(offline.Server, offline.Settings, offline.Table, logger);
        const string step = "broken step";

        await sweeps.IgnoreFailureAsync(step, ThrowUnexpectedAsync, cancellationToken);

        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records);
        var logged = record.GetStructuredStateValue("Step");
        Assert.Equal(EventIds.SweepStepFaulted, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(step, logged);
    }

    #endregion
}
