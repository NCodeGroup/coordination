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
    private static TimeSpan OneMinuteSpan { get; } = TimeSpan.FromMinutes(1);

    private static TimeSpan TenSeconds { get; } = TimeSpan.FromSeconds(10);

    private static TimeToLive OneMinute { get; } = new(OneMinuteSpan);

    private static CoordinationKey LongKey(Keyspace keyspace, int number)
    {
        var prefix = LongPrefixSegment();
        var segments = new List<KeySegment> { prefix };
        for (var index = 0; index < 14; index++)
        {
            var text = new string((char)('a' + index), 256);
            var segment = KeySegment.FromString(text);
            segments.Add(segment);
        }

        var digits = number.ToString("D3", CultureInfo.InvariantCulture);
        var lastText = digits.PadRight(256, 'z');
        var lastSegment = KeySegment.FromString(lastText);
        segments.Add(lastSegment);

        return keyspace.CreateKey(segments);
    }

    private static KeySegment LongPrefixSegment()
    {
        var text = new string('p', 256);
        return KeySegment.FromString(text);
    }

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

        public CoordinationKey KeyOf(string name)
        {
            var segment = KeySegment.FromString(name);
            return Keyspace.CreateKey(segment);
        }

        public async Task<List<T>> QueryAsync<T>(
            string sql,
            Func<NpgsqlDataReader, T> read,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            var commandText = sql.Replace(
                "{schema}",
                $"\"{SchemaName}\"",
                StringComparison.Ordinal
            );
            await using var command = new NpgsqlCommand(commandText, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = read(reader);
                rows.Add(row);
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

            var table = new PgRecordTable(server, keyspaces, settings);
            var pausedTable = new PgRecordTable(pausedServer, pausedKeyspaces, settings);
            var records = new PgRecordService(table);
            var pausedRecords = new PgRecordService(pausedTable);
            var context = new Context(
                servers.Admin19,
                schemaName,
                schema,
                keyspaces,
                pausedServer,
                records,
                pausedRecords
            );

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
                byte[] value = [7, 8, 9];

                var payloads = new ConcurrentQueue<string>();
                await using var listener = new NpgsqlConnection(context.AdminConnectionString);
                await listener.OpenAsync(cancellationToken);
                listener.Notification += (_, e) => payloads.Enqueue(e.Payload);
                await using var listen = new NpgsqlCommand($"LISTEN \"{channel}\"", listener);
                await listen.ExecuteNonQueryAsync(cancellationToken);

                var result = await context.Records.CreateAsync(
                    key,
                    value,
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(result.IsWritten);

                var notified = await listener.WaitAsync(TenSeconds, cancellationToken);
                Assert.True(notified);

                var halfSecond = TimeSpan.FromMilliseconds(500);
                var notifiedAgain = await listener.WaitAsync(halfSecond, cancellationToken);
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
                var oneMinuteLifetime = RecordLifetime.ForTimeToLive(OneMinute);
                byte[] firstValue = [1];
                byte[] secondValue = [2];
                var first = await context.Records.CreateAsync(
                    key,
                    firstValue,
                    oneMinuteLifetime,
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
                    secondValue,
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(second.IsWritten);

                var firstRevision = first.Revision.Value.ToString(CultureInfo.InvariantCulture);
                var sql =
                    "SELECT revision, kind FROM {schema}.change_items WHERE revision > "
                    + firstRevision
                    + " ORDER BY revision, ordinal";
                var items = await context.QueryAsync(
                    sql,
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
                var oneMinuteLifetime = RecordLifetime.ForTimeToLive(OneMinute);
                byte[] createdValue = [1];
                byte[] swappedValue = [2];
                var created = await context.Records.CreateAsync(
                    key,
                    createdValue,
                    oneMinuteLifetime,
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
                            swappedValue,
                            RecordLifetime.Persistent,
                            null,
                            cancellationToken
                        )
                        .AsTask();

                    await reached.Task.WaitAsync(TenSeconds, cancellationToken);

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

                    var swapped = await swap.WaitAsync(TenSeconds, cancellationToken);
                    Assert.True(swapped.IsWritten);

                    var record = await read.WaitAsync(TenSeconds, cancellationToken);
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
    public async Task ListAsync_RecordExpiresWhileASwapIsPaused_WaitsForTheSwap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var prefixSegment = KeySegment.FromString("listed");
                var swappedSegment = KeySegment.FromString("swapped");
                var key = context.Keyspace.CreateKey(prefixSegment, swappedSegment);
                var prefix = context.Keyspace.CreateKey(prefixSegment);
                var oneMinuteLifetime = RecordLifetime.ForTimeToLive(OneMinute);
                byte[] createdValue = [1];
                byte[] swappedValue = [2];
                var created = await context.Records.CreateAsync(
                    key,
                    createdValue,
                    oneMinuteLifetime,
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
                            swappedValue,
                            RecordLifetime.Persistent,
                            null,
                            cancellationToken
                        )
                        .AsTask();

                    await reached.Task.WaitAsync(TenSeconds, cancellationToken);

                    await PgServers.ExpireRowsAsync(
                        context.AdminConnectionString,
                        context.SchemaName,
                        "records",
                        cancellationToken
                    );

                    var request = new RecordPageRequest(10);
                    var list = context
                        .Records.ListAsync(prefix, request, null, cancellationToken)
                        .AsTask();

                    await PgActivity.WaitForLockWaitAsync(
                        context.AdminConnectionString,
                        context.SchemaName,
                        cancellationToken
                    );
                    Assert.False(list.IsCompleted);

                    gate.TrySetResult();

                    var swapped = await swap.WaitAsync(TenSeconds, cancellationToken);
                    Assert.True(swapped.IsWritten);

                    var page = await list.WaitAsync(TenSeconds, cancellationToken);
                    var record = Assert.Single(page.Records);
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

    [PgServerFact]
    public async Task ListAsync_LongestKeys_PagesThroughEveryRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRecordsAsync(
            async context =>
            {
                var expected = new HashSet<string>(StringComparer.Ordinal);
                byte[] value = [1];
                for (var number = 0; number < 30; number++)
                {
                    var key = LongKey(context.Keyspace, number);
                    var result = await context.Records.CreateAsync(
                        key,
                        value,
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                    Assert.True(result.IsWritten);
                    var keyText = PgKeys.KeyText(key);
                    expected.Add(keyText);
                }

                var prefixSegment = LongPrefixSegment();
                var prefix = context.Keyspace.CreateKey(prefixSegment);
                var listed = new List<string>();
                string? token = null;
                do
                {
                    var request = new RecordPageRequest(7) { ContinuationToken = token };
                    var page = await context.Records.ListAsync(
                        prefix,
                        request,
                        null,
                        cancellationToken
                    );

                    Assert.True(page.Records.Length <= 7);
                    var pageKeyTexts = page.Records.Select(record => PgKeys.KeyText(record.Key));
                    listed.AddRange(pageKeyTexts);

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
