// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sessions;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgChangeLogTests(PgServers servers)
{
    private static readonly TimeToLive OneMinute = new(TimeSpan.FromMinutes(1));

    private sealed record Context(
        PgKeyspaces Keyspaces,
        PgChangeLog Log,
        PgSessionService Sessions,
        PgRecordService Records
    )
    {
        public Keyspace Keyspace { get; } = new("change-log");

        public CoordinationKey KeyOf(string name) =>
            Keyspace.CreateKey(KeySegment.FromString(name));

        public async Task<PgLogRead<PgChange>> ReadAsync(CancellationToken cancellationToken)
        {
            var id = await Keyspaces.ResolveAsync(Keyspace, cancellationToken);
            return await Log.ReadAsync(id, 0, 100, cancellationToken);
        }
    }

    private async Task WithServicesAsync(
        Func<Context, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "changelog_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var settings = PgSettings.Default with { Schema = schema };

        try
        {
            await using (var source = PgSource.FromConnectionString(servers.PostgreSql19))
            {
                var server = new PgServer(source, settings);
                var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
                await probe.RunAsync(cancellationToken);

                var keyspaces = new PgKeyspaces(server, schema);
                var table = new PgRecordTable(server, keyspaces, settings);

                await test(
                    new Context(
                        keyspaces,
                        new PgChangeLog(server, schema),
                        new PgSessionService(table, new PgRandomTokens()),
                        new PgRecordService(table)
                    )
                );
            }
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

    #region ReadAsync Tests

    [PgServerFact]
    public async Task ReadAsync_AfterTwoWrites_ReturnsBothEntriesWithTheirItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var first = await context.Records.CreateAsync(
                    context.KeyOf("one"),
                    new byte[] { 1 },
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                var second = await context.Records.CreateAsync(
                    context.KeyOf("two"),
                    new byte[] { 2, 3 },
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(first.IsWritten);
                Assert.True(second.IsWritten);

                var read = await context.ReadAsync(cancellationToken);

                Assert.Equal(1, read.Removed);
                Assert.Equal(second.Revision.Value, read.Current);
                Assert.Equal(2, read.Entries.Length);

                Assert.Equal(first.Revision.Value, read.Entries[0].Number);
                var firstItem = Assert.Single(read.Entries[0].Items);
                Assert.Equal((short)1, firstItem.Kind);
                Assert.Equal(PgKeys.KeyText(context.KeyOf("one")), firstItem.Key);
                Assert.Equal(new byte[] { 1 }, firstItem.Value);

                Assert.Equal(second.Revision.Value, read.Entries[1].Number);
                var secondItem = Assert.Single(read.Entries[1].Items);
                Assert.Equal((short)1, secondItem.Kind);
                Assert.Equal(PgKeys.KeyText(context.KeyOf("two")), secondItem.Key);
                Assert.Equal(new byte[] { 2, 3 }, secondItem.Value);

                Assert.All(read.Entries, entry => Assert.Null(entry.EndedSession));
                Assert.All(read.Entries, entry => Assert.Empty(entry.EndedKeys));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ReadAsync_SessionEnd_ListsTheKeysBoundToIt()
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

                var keys = new[] { context.KeyOf("a"), context.KeyOf("b"), context.KeyOf("c") };
                foreach (var key in keys)
                {
                    var created = await context.Records.CreateAsync(
                        key,
                        new byte[] { 1 },
                        lifetime,
                        null,
                        cancellationToken
                    );
                    Assert.True(created.IsWritten);
                }

                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);

                var read = await context.ReadAsync(cancellationToken);

                var ended = Assert.Single(read.Entries, entry => entry.EndedSession is not null);
                Assert.Equal(grant.Id.Value, ended.EndedSession);
                Assert.Equal(read.Current, ended.Number);
                Assert.Empty(ended.Items);
                Assert.Equal(
                    keys.Select(PgKeys.KeyText).Order(StringComparer.Ordinal),
                    ended.EndedKeys.Select(item => item.Key).Order(StringComparer.Ordinal)
                );
                Assert.All(ended.EndedKeys, item => Assert.Equal((short)2, item.Kind));
                Assert.All(ended.EndedKeys, item => Assert.Null(item.Value));
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ReadAsync_SessionEndThenReplacement_StillListsTheReplacedKey()
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

                var first = await context.Records.CreateAsync(
                    key,
                    new byte[] { 1 },
                    RecordLifetime.ForSession(grant.Id),
                    null,
                    cancellationToken
                );
                Assert.True(first.IsWritten);

                await context.Sessions.CloseAsync(grant.Id, null, cancellationToken);

                var second = await context.Records.CreateAsync(
                    key,
                    new byte[] { 2 },
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
                Assert.True(second.IsWritten);

                var read = await context.ReadAsync(cancellationToken);

                var ended = Assert.Single(read.Entries, entry => entry.EndedSession is not null);
                var listed = Assert.Single(ended.EndedKeys);
                Assert.Equal(PgKeys.KeyText(key), listed.Key);
                Assert.Equal((short)2, listed.Kind);
            },
            cancellationToken
        );
    }

    #endregion
}
