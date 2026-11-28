// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Sessions.Tests;

public sealed class PgSessionServerTests(PgServers servers)
{
    private static TimeSpan OneMinuteSpan { get; } = TimeSpan.FromMinutes(1);

    private static TimeToLive OneMinute { get; } = new(OneMinuteSpan);

    private sealed record Context(
        string AdminConnectionString,
        string SchemaName,
        PgSessionService Sessions,
        PgRecordService Records
    )
    {
        public Keyspace Keyspace { get; } = new("sessions-server");

        public CoordinationKey KeyOf(string name)
        {
            var segment = KeySegment.FromString(name);
            return Keyspace.CreateKey(segment);
        }

        public async ValueTask<List<T>> QueryAsync<T>(
            string sql,
            Func<NpgsqlDataReader, T> read,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            var text = sql.Replace("{schema}", $"\"{SchemaName}\"", StringComparison.Ordinal);
            await using var command = new NpgsqlCommand(text, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = read(reader);
                rows.Add(row);
            }

            return rows;
        }

        public async ValueTask<long> RevisionAsync(CancellationToken cancellationToken)
        {
            var revisions = await QueryAsync(
                "SELECT revision FROM {schema}.keyspaces",
                reader => reader.GetInt64(0),
                cancellationToken
            );
            return Assert.Single(revisions);
        }
    }

    private async ValueTask WithServicesAsync(
        Func<Context, ValueTask> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "sessions_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var settings = PgSettings.Default with { Schema = schema };

        try
        {
            await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
            var server = new PgServer(source, settings);
            var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
            await probe.RunAsync(cancellationToken);

            var tokens = new PgRandomTokens();
            var keyspaces = new PgKeyspaces(server, schema, tokens);
            var table = new PgRecordTable(server, keyspaces, settings);
            var polling = new PgPolling(TimeProvider.System);

            var sessions = new PgSessionService(table, tokens, polling);
            var records = new PgRecordService(table);
            var context = new Context(servers.Admin19, schemaName, sessions, records);

            await test(context);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE",
                CancellationToken.None
            );
        }
    }

    #region CloseAsync Tests

    [PgServerFact]
    public async Task CloseAsync_SessionWithThreeRecords_TakesOneRevisionAndLogsOneEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    OneMinute,
                    null,
                    cancellationToken
                );
                var lifetime = RecordLifetime.ForSession(grant.Id);

                CoordinationKey[] keys =
                [
                    context.KeyOf("a"),
                    context.KeyOf("b"),
                    context.KeyOf("c"),
                ];
                byte[] value = [1];
                foreach (var key in keys)
                {
                    var created = await context.Records.CreateAsync(
                        key,
                        value,
                        lifetime,
                        null,
                        cancellationToken
                    );
                    Assert.True(created.IsWritten);
                }

                var before = await context.RevisionAsync(cancellationToken);

                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);

                var after = await context.RevisionAsync(cancellationToken);
                Assert.Equal(before + 1, after);

                var entries = await context.QueryAsync(
                    "SELECT revision, ended_session FROM {schema}.changes WHERE ended_session IS NOT NULL",
                    reader => (Revision: reader.GetInt64(0), Session: reader.GetString(1)),
                    cancellationToken
                );
                var entry = Assert.Single(entries);
                Assert.Equal(after, entry.Revision);
                Assert.Equal(grant.Id.Value, entry.Session);

                foreach (var key in keys)
                {
                    var record = await context.Records.ReadAsync(key, null, cancellationToken);
                    Assert.Null(record);
                }

                var rows = await context.QueryAsync(
                    "SELECT session_id FROM {schema}.records",
                    reader => reader.GetString(0),
                    cancellationToken
                );
                Assert.Equal(3, rows.Count);
                Assert.All(rows, row => Assert.Equal(grant.Id.Value, row));
            },
            cancellationToken
        );
    }

    #endregion

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_BoundToAnExpiredSession_EndsItAndReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    OneMinute,
                    null,
                    cancellationToken
                );

                await PgServers.ExpireRowsAsync(
                    context.AdminConnectionString,
                    context.SchemaName,
                    "sessions",
                    cancellationToken
                );

                var key = context.KeyOf("late");
                var lifetime = RecordLifetime.ForSession(grant.Id);
                byte[] value = [1];

                var result = await context.Records.CreateAsync(
                    key,
                    value,
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.Equal(RecordWriteStatus.SessionEnded, result.Status);

                var entries = await context.QueryAsync(
                    "SELECT ended_session FROM {schema}.changes WHERE ended_session IS NOT NULL",
                    reader => reader.GetString(0),
                    cancellationToken
                );
                Assert.Equal([grant.Id.Value], entries);

                var sessions = await context.QueryAsync(
                    "SELECT id FROM {schema}.sessions",
                    reader => reader.GetString(0),
                    cancellationToken
                );
                Assert.Empty(sessions);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task CreateAsync_ReplacingARecordOfAnEndedSession_NamesTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    OneMinute,
                    null,
                    cancellationToken
                );
                var key = context.KeyOf("replaced");
                var lifetime = RecordLifetime.ForSession(grant.Id);
                byte[] firstValue = [1];
                byte[] secondValue = [2];

                var first = await context.Records.CreateAsync(
                    key,
                    firstValue,
                    lifetime,
                    null,
                    cancellationToken
                );
                Assert.True(first.IsWritten);

                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);

                var second = await context.Records.CreateAsync(
                    key,
                    secondValue,
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(second.IsWritten);

                var items = await context.QueryAsync(
                    "SELECT revision, kind, replaced_session FROM {schema}.change_items "
                        + "WHERE replaced_session IS NOT NULL",
                    reader =>
                        (
                            Revision: reader.GetInt64(0),
                            Kind: reader.GetInt16(1),
                            Replaced: reader.GetString(2)
                        ),
                    cancellationToken
                );
                var item = Assert.Single(items);
                Assert.Equal(second.Revision.Value, item.Revision);
                Assert.Equal((short)1, item.Kind);
                Assert.Equal(grant.Id.Value, item.Replaced);
            },
            cancellationToken
        );
    }

    #endregion
}
