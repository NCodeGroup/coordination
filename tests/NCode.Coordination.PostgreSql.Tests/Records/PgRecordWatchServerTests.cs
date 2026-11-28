// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Conformance;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordWatchServerTests(PgServers servers)
{
    private static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    private static TimeSpan OneMinuteSpan { get; } = TimeSpan.FromMinutes(1);

    private static TimeToLive OneMinute { get; } = new(OneMinuteSpan);

    private static async Task<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(timeout, cancellationToken);
        Assert.True(moved);
        return enumerator.Current;
    }

    private static Task<RecordChange> NextAsync(
        IAsyncEnumerator<RecordChange> enumerator,
        CancellationToken cancellationToken
    ) => NextAsync(enumerator, Patience, cancellationToken);

    private static async Task<Revision> CreateAsync(
        Context context,
        CoordinationKey key,
        byte value,
        RecordLifetime lifetime,
        CancellationToken cancellationToken
    )
    {
        var result = await context.Records.CreateAsync(
            key,
            new byte[] { value },
            lifetime,
            null,
            cancellationToken
        );
        Assert.True(result.IsWritten);
        return result.Revision;
    }

    private static async Task<Revision> SwapAsync(
        Context context,
        CoordinationKey key,
        Revision expected,
        byte value,
        CancellationToken cancellationToken
    )
    {
        var result = await context.Records.CompareAndSwapAsync(
            key,
            expected,
            new byte[] { value },
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(result.IsWritten);
        return result.Revision;
    }

    private sealed record Context(
        string AdminConnectionString,
        string SchemaName,
        PostgreSqlProvider Provider
    )
    {
        public Keyspace Keyspace { get; } = new("watch-server");

        public IRecordService Records => ServiceOf<IRecordService>();

        public IRecordWatchService Watches => ServiceOf<IRecordWatchService>();

        public ISessionService Sessions => ServiceOf<ISessionService>();

        public INotificationService Notifications => ServiceOf<INotificationService>();

        private T ServiceOf<T>()
            where T : class
        {
            var service = Provider.GetPrimitiveService(typeof(T));
            return Assert.IsAssignableFrom<T>(service);
        }

        public async Task<long> LogRemovedAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                $"SELECT log_removed FROM \"{SchemaName}\".keyspaces",
                connection
            );

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return Assert.IsType<long>(scalar);
        }

        public async Task InsertMessageWithoutWakeUpAsync(
            CoordinationKey topic,
            byte[] payload,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var find = new NpgsqlCommand(
                $"SELECT id FROM \"{SchemaName}\".keyspaces WHERE name = $1",
                connection
            )
            {
                Parameters = { new NpgsqlParameter { Value = PgKeys.KeyspaceName(Keyspace) } },
            };
            var foundId = await find.ExecuteScalarAsync(cancellationToken);
            var keyspaceId = Assert.IsType<long>(foundId);

            var index = PgKeys.Index(topic);
            var depth = (short)topic.PartitionDepth;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using var raise = new NpgsqlCommand(
                $"UPDATE \"{SchemaName}\".topics SET sequence = sequence + 1 "
                    + "WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 RETURNING sequence",
                connection,
                transaction
            )
            {
                Parameters =
                {
                    new NpgsqlParameter { Value = keyspaceId },
                    new NpgsqlParameter { Value = index },
                    new NpgsqlParameter { Value = depth },
                },
            };
            var raised = await raise.ExecuteScalarAsync(cancellationToken);
            var sequence = Assert.IsType<long>(raised);

            await using var insert = new NpgsqlCommand(
                $"INSERT INTO \"{SchemaName}\".messages (keyspace_id, idx, depth, sequence, payload, deadline) "
                    + "VALUES ($1, $2, $3, $4, $5, clock_timestamp() + interval '5 minutes')",
                connection,
                transaction
            )
            {
                Parameters =
                {
                    new NpgsqlParameter { Value = keyspaceId },
                    new NpgsqlParameter { Value = index },
                    new NpgsqlParameter { Value = depth },
                    new NpgsqlParameter { Value = sequence },
                    new NpgsqlParameter { Value = payload },
                },
            };
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task WithProviderAsync(
        Func<Context, Task> test,
        CancellationToken cancellationToken,
        TimeSpan? logRetention = null
    )
    {
        var schemaName = "watch_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var settings = PgConformance.Settings with { Schema = PgSchemaName.Create(schemaName) };
        if (logRetention is { } retention)
        {
            settings = settings with { LogRetention = retention };
        }

        try
        {
            var source = PgSource.FromConnectionString(servers.PostgreSql19);
            await using var provider = new PostgreSqlProvider(
                source,
                PostgreSqlProfiles.Standalone,
                settings
            );
            await provider.ProbeAsync(cancellationToken);

            var context = new Context(servers.Admin19, schemaName, provider);
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

    #region WatchKeyAsync Tests

    [PgServerFact]
    public async Task WatchKeyAsync_AfterARevisionTheLogHolds_ReplaysWithoutAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync(
            async context =>
            {
                var key = context.Keyspace.CreateKey("replayed");
                var first = await CreateAsync(
                    context,
                    key,
                    1,
                    RecordLifetime.Persistent,
                    cancellationToken
                );
                var second = await SwapAsync(context, key, first, 2, cancellationToken);
                var third = await SwapAsync(context, key, second, 3, cancellationToken);

                await using var changes = context
                    .Watches.WatchKeyAsync(key, first, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                var change = await NextAsync(changes, cancellationToken);
                Assert.False(change.IsResync);
                Assert.Equal(RecordChangeKind.Written, change.Kind);
                Assert.Equal(second, change.Record?.Revision);
                var secondValue = change.Record?.Value.ToArray();
                Assert.Equal(new byte[] { 2 }, secondValue);

                change = await NextAsync(changes, cancellationToken);
                Assert.False(change.IsResync);
                Assert.Equal(RecordChangeKind.Written, change.Kind);
                Assert.Equal(third, change.Record?.Revision);
                var thirdValue = change.Record?.Value.ToArray();
                Assert.Equal(new byte[] { 3 }, thirdValue);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task WatchKeyAsync_AfterARevisionTheLogDropped_StartsWithAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var oneSecond = TimeSpan.FromSeconds(1);
        await WithProviderAsync(
            async context =>
            {
                var key = context.Keyspace.CreateKey("dropped");
                var first = await CreateAsync(
                    context,
                    key,
                    1,
                    RecordLifetime.Persistent,
                    cancellationToken
                );
                var second = await SwapAsync(context, key, first, 2, cancellationToken);

                using var patience = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                patience.CancelAfter(Patience);
                var pollInterval = TimeSpan.FromMilliseconds(200);
                while (await context.LogRemovedAsync(patience.Token) <= first.Value)
                {
                    await Task.Delay(pollInterval, patience.Token);
                }

                await using var changes = context
                    .Watches.WatchKeyAsync(key, first, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                var change = await NextAsync(changes, cancellationToken);
                Assert.True(change.IsResync);
                Assert.True(change.Revision >= second);
            },
            cancellationToken,
            oneSecond
        );
    }

    #endregion

    #region WatchPrefixAsync Tests

    [PgServerFact]
    public async Task WatchPrefixAsync_SessionEnds_DeliversTheDeletionOfEachBoundRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync(
            async context =>
            {
                var prefix = context.Keyspace.CreateKey("bound");
                var keyA = context.Keyspace.CreateKey("bound", "a");
                var keyB = context.Keyspace.CreateKey("bound", "b");

                var grant = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    OneMinute,
                    null,
                    cancellationToken
                );
                var lifetime = RecordLifetime.ForSession(grant.Id);

                await using var changes = context
                    .Watches.WatchPrefixAsync(prefix, null, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                var change = await NextAsync(changes, cancellationToken);
                Assert.True(change.IsResync);

                var revisionA = await CreateAsync(context, keyA, 1, lifetime, cancellationToken);
                var revisionB = await CreateAsync(context, keyB, 2, lifetime, cancellationToken);

                change = await NextAsync(changes, cancellationToken);
                Assert.Equal(RecordChangeKind.Written, change.Kind);
                Assert.Equal(revisionA, change.Record?.Revision);

                change = await NextAsync(changes, cancellationToken);
                Assert.Equal(RecordChangeKind.Written, change.Kind);
                Assert.Equal(revisionB, change.Record?.Revision);

                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);

                var firstDeleted = await NextAsync(changes, cancellationToken);
                var secondDeleted = await NextAsync(changes, cancellationToken);
                var deleted = new List<RecordChange> { firstDeleted, secondDeleted };

                Assert.All(deleted, item => Assert.Equal(RecordChangeKind.Deleted, item.Kind));
                Assert.All(deleted, item => Assert.True(item.Revision > revisionB));
                var expectedKeys = new HashSet<CoordinationKey> { keyA, keyB };
                var deletedKeys = deleted.Select(item => item.Key).OfType<CoordinationKey>();
                var deletedSet = deletedKeys.ToHashSet();
                Assert.Equal(expectedKeys, deletedSet);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task WatchPrefixAsync_TwoWatchesOfAKeyspace_EachDeliversTheWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync(
            async context =>
            {
                var prefix = context.Keyspace.CreateKey("shared");
                var key = context.Keyspace.CreateKey("shared", "a");

                await using var keyWatch = context
                    .Watches.WatchKeyAsync(key, null, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                await using var prefixWatch = context
                    .Watches.WatchPrefixAsync(prefix, null, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                var keyResync = await NextAsync(keyWatch, cancellationToken);
                var prefixResync = await NextAsync(prefixWatch, cancellationToken);

                Assert.True(keyResync.IsResync);
                Assert.True(prefixResync.IsResync);

                var revision = await CreateAsync(
                    context,
                    key,
                    1,
                    RecordLifetime.Persistent,
                    cancellationToken
                );

                var fromKey = await NextAsync(keyWatch, cancellationToken);
                Assert.Equal(RecordChangeKind.Written, fromKey.Kind);
                Assert.Equal(revision, fromKey.Record?.Revision);

                var fromPrefix = await NextAsync(prefixWatch, cancellationToken);
                Assert.Equal(RecordChangeKind.Written, fromPrefix.Kind);
                Assert.Equal(revision, fromPrefix.Record?.Revision);
            },
            cancellationToken
        );
    }

    #endregion

    #region SubscribeAsync Tests

    [PgServerFact]
    public async Task SubscribeAsync_WakeUpLost_DeliversWithinTheCheckInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var twoChecks = PgConformance.Settings.CheckInterval * 2;
        await WithProviderAsync(
            async context =>
            {
                var topic = context.Keyspace.CreateKey("lost");

                await using var notifications = context
                    .Notifications.SubscribeAsync(topic, null, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                var notification = await NextAsync(notifications, Patience, cancellationToken);
                Assert.True(notification.IsResync);

                await context.Notifications.PublishAsync(
                    topic,
                    new byte[] { 1 },
                    null,
                    cancellationToken
                );

                notification = await NextAsync(notifications, Patience, cancellationToken);
                Assert.Equal(NotificationKind.Message, notification.Kind);
                Assert.Equal(1, notification.Sequence);

                await context.InsertMessageWithoutWakeUpAsync(
                    topic,
                    new byte[] { 2 },
                    cancellationToken
                );

                notification = await NextAsync(notifications, twoChecks, cancellationToken);
                Assert.Equal(NotificationKind.Message, notification.Kind);
                Assert.Equal(2, notification.Sequence);
                var payload = notification.Payload.ToArray();
                Assert.Equal(new byte[] { 2 }, payload);
            },
            cancellationToken
        );
    }

    #endregion
}
