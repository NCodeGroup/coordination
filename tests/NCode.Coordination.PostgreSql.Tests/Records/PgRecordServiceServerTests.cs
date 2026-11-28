// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordServiceServerTests(PgServers servers)
{
    private static readonly TimeToLive OneMinute = new(TimeSpan.FromMinutes(1));

    private static CoordinationKey LongKey(Keyspace keyspace, int number)
    {
        var segments = new List<KeySegment> { LongPrefixSegment() };
        for (var index = 0; index < 14; index++)
        {
            segments.Add(KeySegment.FromString(new string((char)('a' + index), 256)));
        }

        segments.Add(
            KeySegment.FromString(
                number.ToString("D3", CultureInfo.InvariantCulture).PadRight(256, 'z')
            )
        );

        return keyspace.CreateKey(segments);
    }

    private static KeySegment LongPrefixSegment() => KeySegment.FromString(new string('p', 256));

    private sealed record Context(
        string AdminConnectionString,
        string SchemaName,
        PgSchemaName Schema,
        PgKeyspaces Keyspaces,
        PgServer PausedServer,
        PgRecordService Records,
        PgRecordService PausedRecords
    )
    {
        public Keyspace Keyspace { get; } = new("records-server");

        public CoordinationKey KeyOf(string name) =>
            Keyspace.CreateKey(KeySegment.FromString(name));

        public async Task<List<T>> QueryAsync<T>(
            string sql,
            Func<NpgsqlDataReader, T> read,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                sql.Replace("{schema}", $"\"{SchemaName}\"", StringComparison.Ordinal),
                connection
            );
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(read(reader));
            }

            return rows;
        }

        public async Task<long> KeyspaceIdAsync(CancellationToken cancellationToken)
        {
            var ids = await QueryAsync(
                "SELECT id FROM {schema}.keyspaces",
                reader => reader.GetInt64(0),
                cancellationToken
            );
            return Assert.Single(ids);
        }
    }

    private async Task WithRecordsAsync(
        Func<Context, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "records_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
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
            var pausedServer = new PgServer(source, settings);
            var pausedKeyspaces = new PgKeyspaces(pausedServer, schema, tokens);

            await test(
                new Context(
                    servers.Admin19,
                    schemaName,
                    schema,
                    keyspaces,
                    pausedServer,
                    new PgRecordService(new PgRecordTable(server, keyspaces, settings)),
                    new PgRecordService(new PgRecordTable(pausedServer, pausedKeyspaces, settings))
                )
            );
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

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_Written_LogsOneEntryAndNotifiesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var key = context.KeyOf("one");
                var keyspaceId = await context.Keyspaces.ResolveAsync(
                    key.Keyspace,
                    cancellationToken
                );
                var channel = PgChannels.ForKeyspace(context.Schema, keyspaceId);

                var payloads = new ConcurrentQueue<string>();
                await using var listener = new NpgsqlConnection(context.AdminConnectionString);
                await listener.OpenAsync(cancellationToken);
                listener.Notification += (_, e) => payloads.Enqueue(e.Payload);
                await using var listen = new NpgsqlCommand($"LISTEN \"{channel}\"", listener);
                await listen.ExecuteNonQueryAsync(cancellationToken);

                var result = await context.Records.CreateAsync(
                    key,
                    new byte[] { 7, 8, 9 },
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);

                var notified = await listener.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    cancellationToken
                );
                Assert.True(notified);

                var notifiedAgain = await listener.WaitAsync(
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken
                );
                Assert.False(notifiedAgain);

                var expectedPayload = PgChannels.ChangePayload(result.Revision.Value);
                var receivedPayloads = payloads.ToArray();
                Assert.Equal([expectedPayload], receivedPayloads);

                var entries = await context.QueryAsync(
                    "SELECT revision FROM {schema}.changes",
                    reader => reader.GetInt64(0),
                    cancellationToken
                );
                Assert.Equal([result.Revision.Value], entries);

                var items = await context.QueryAsync(
                    "SELECT revision, kind, value FROM {schema}.change_items ORDER BY ordinal",
                    reader =>
                        (
                            Revision: reader.GetInt64(0),
                            Kind: reader.GetInt16(1),
                            Value: reader.GetFieldValue<byte[]>(2)
                        ),
                    cancellationToken
                );
                var item = Assert.Single(items);
                Assert.Equal(result.Revision.Value, item.Revision);
                Assert.Equal((short)1, item.Kind);
                Assert.Equal(new byte[] { 7, 8, 9 }, item.Value);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task CreateAsync_OverAnExpiredRecord_AnnouncesItsDeletionFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var key = context.KeyOf("expiring");
                var first = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    RecordLifetime.ForTimeToLive(OneMinute),
                    null,
                    cancellationToken
                );
                Assert.True(first.IsWritten);

                await PgServers.ExpireRowsAsync(
                    context.AdminConnectionString,
                    context.SchemaName,
                    "records",
                    cancellationToken
                );

                var second = await context.Records.CreateAsync(
                    key,
                    new byte[] { 2 },
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(second.IsWritten);

                var items = await context.QueryAsync(
                    "SELECT revision, kind FROM {schema}.change_items WHERE revision > "
                        + first.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + " ORDER BY revision, ordinal",
                    reader => (Revision: reader.GetInt64(0), Kind: reader.GetInt16(1)),
                    cancellationToken
                );

                Assert.Equal(2, items.Count);
                Assert.Equal((short)2, items[0].Kind);
                Assert.Equal((short)1, items[1].Kind);
                Assert.Equal(first.Revision.Value + 1, items[0].Revision);
                Assert.Equal(second.Revision.Value, items[1].Revision);
                Assert.True(items[0].Revision < items[1].Revision);
            },
            cancellationToken
        );
    }

    #endregion

    #region ReadAsync Tests

    [PgServerFact]
    public async Task ReadAsync_RecordExpiresWhileASwapIsPaused_WaitsForTheSwap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var key = context.KeyOf("swapped");
                var created = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    RecordLifetime.ForTimeToLive(OneMinute),
                    null,
                    cancellationToken
                );
                Assert.True(created.IsWritten);

                var reached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                var gate = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                context.PausedServer.AfterFirstBatch = async token =>
                {
                    reached.TrySetResult();
                    await gate.Task.WaitAsync(token);
                };

                try
                {
                    var swap = context
                        .PausedRecords.CompareAndSwapAsync(
                            key,
                            created.Revision,
                            new byte[] { 2 },
                            RecordLifetime.Persistent,
                            null,
                            cancellationToken
                        )
                        .AsTask();

                    await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                    await PgServers.ExpireRowsAsync(
                        context.AdminConnectionString,
                        context.SchemaName,
                        "records",
                        cancellationToken
                    );

                    var read = context.Records.ReadAsync(key, null, cancellationToken).AsTask();

                    await PgActivity.WaitForLockWaitAsync(
                        context.AdminConnectionString,
                        context.SchemaName,
                        cancellationToken
                    );
                    Assert.False(read.IsCompleted);

                    gate.TrySetResult();

                    var swapped = await swap.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    Assert.True(swapped.IsWritten);

                    var record = await read.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    Assert.NotNull(record);
                    var recordValue = record.Value.ToArray();
                    Assert.Equal(new byte[] { 2 }, recordValue);
                    Assert.Equal(swapped.Revision.Value, record.Revision.Value);
                }
                finally
                {
                    gate.TrySetResult();
                }
            },
            cancellationToken
        );
    }

    #endregion

    #region ListAsync Tests

    [PgServerFact]
    public async Task ListAsync_LongestKeys_PagesThroughEveryRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var expected = new HashSet<string>(StringComparer.Ordinal);
                for (var number = 0; number < 30; number++)
                {
                    var key = LongKey(context.Keyspace, number);
                    var result = await context.Records.CreateAsync(
                        key,
                        new byte[] { 1 },
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                    Assert.True(result.IsWritten);
                    expected.Add(PgKeys.KeyText(key));
                }

                var prefix = context.Keyspace.CreateKey(LongPrefixSegment());
                var listed = new List<string>();
                string? token = null;
                do
                {
                    var page = await context.Records.ListAsync(
                        prefix,
                        new RecordPageRequest(7) { ContinuationToken = token },
                        null,
                        cancellationToken
                    );

                    Assert.True(page.Records.Length <= 7);
                    listed.AddRange(page.Records.Select(record => PgKeys.KeyText(record.Key)));

                    token = page.ContinuationToken;
                    if (token is not null)
                    {
                        Assert.True(token.Length <= 684);
                    }
                } while (token is not null);

                Assert.Equal(30, listed.Count);
                var listedSet = listed.ToHashSet(StringComparer.Ordinal);
                Assert.Equal(expected, listedSet);
            },
            cancellationToken
        );
    }

    #endregion
}
