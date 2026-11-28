// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Sessions;
using NCode.Coordination.PostgreSql.Sweep;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Leases.Tests;

public sealed class PgGrantsServerTests(PgServers servers)
{
    private static readonly TimeToLive OneMinute = new(TimeSpan.FromMinutes(1));

    private static LeaseGrant GrantOf(LeaseAcquireResult result)
    {
        Assert.True(result.IsAcquired);
        return result.Grant ?? throw new InvalidOperationException("The result holds no grant.");
    }

    private sealed record Context(
        string AdminConnectionString,
        string SchemaName,
        PgLeaseService Leases,
        PgSessionService Sessions,
        PgSweeps Sweeps
    )
    {
        public Keyspace Keyspace { get; } = new("grants-server");

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

        public async Task<long> RevisionAsync(CancellationToken cancellationToken)
        {
            var revisions = await QueryAsync(
                "SELECT revision FROM {schema}.keyspaces",
                reader => reader.GetInt64(0),
                cancellationToken
            );
            return Assert.Single(revisions);
        }

        public async Task<long> LastFencingAsync(CancellationToken cancellationToken)
        {
            var lasts = await QueryAsync(
                "SELECT last FROM {schema}.fencing",
                reader => reader.GetInt64(0),
                cancellationToken
            );
            return Assert.Single(lasts);
        }

        public async Task<int> GrantCountAsync(CancellationToken cancellationToken)
        {
            var counts = await QueryAsync(
                "SELECT count(*)::int FROM {schema}.grants",
                reader => reader.GetInt32(0),
                cancellationToken
            );
            return Assert.Single(counts);
        }
    }

    private async Task WithServicesAsync(
        Func<Context, Task> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "grants_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
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
                var tokens = new PgRandomTokens();

                await test(
                    new Context(
                        servers.Admin19,
                        schemaName,
                        new PgLeaseService(new PgGrants(table), tokens),
                        new PgSessionService(table, tokens),
                        new PgSweeps(server, settings, table)
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

    #region TryAcquireAsync Tests

    [PgServerFact]
    public async Task TryAcquireAsync_AfterTheGrantExpiredAndWasSwept_GivesAGreaterFencingToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var resource = context.KeyOf("swept");
                var terms = LeaseTerms.ForTimeToLive(OneMinute);

                var first = await context.Leases.TryAcquireAsync(
                    resource,
                    terms,
                    null,
                    cancellationToken
                );
                var firstToken = GrantOf(first).FencingToken.Value;

                await PgServers.ExpireRowsAsync(
                    context.AdminConnectionString,
                    context.SchemaName,
                    "grants",
                    cancellationToken
                );

                await context.Sweeps.SweepAsync(cancellationToken);

                var grantCount = await context.GrantCountAsync(cancellationToken);
                var lastAfterSweep = await context.LastFencingAsync(cancellationToken);

                Assert.Equal(0, grantCount);
                Assert.Equal(firstToken, lastAfterSweep);

                var second = await context.Leases.TryAcquireAsync(
                    resource,
                    terms,
                    null,
                    cancellationToken
                );
                var secondToken = GrantOf(second).FencingToken.Value;
                var lastAfterSecond = await context.LastFencingAsync(cancellationToken);

                Assert.True(secondToken > firstToken);
                Assert.Equal(secondToken, lastAfterSecond);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TryAcquireAsync_Granted_TakesNoRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var resource = context.KeyOf("no-revision");

                var first = await context.Leases.TryAcquireAsync(
                    resource,
                    LeaseTerms.ForTimeToLive(OneMinute),
                    null,
                    cancellationToken
                );
                var grant = GrantOf(first);
                var before = await context.RevisionAsync(cancellationToken);

                var renewed = await context.Leases.RenewAsync(
                    grant,
                    OneMinute,
                    null,
                    cancellationToken
                );
                var afterRenew = await context.RevisionAsync(cancellationToken);

                Assert.True(renewed);
                Assert.Equal(before, afterRenew);

                var released = await context.Leases.ReleaseAsync(grant, null, cancellationToken);
                var afterRelease = await context.RevisionAsync(cancellationToken);

                Assert.True(released);
                Assert.Equal(before, afterRelease);

                var second = await context.Leases.TryAcquireAsync(
                    resource,
                    LeaseTerms.ForTimeToLive(OneMinute),
                    null,
                    cancellationToken
                );
                var afterSecond = await context.RevisionAsync(cancellationToken);

                Assert.True(second.IsAcquired);
                Assert.Equal(before, afterSecond);
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task TryAcquireAsync_HolderSessionClosed_GrantsTheNext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServicesAsync(
            async context =>
            {
                var resource = context.KeyOf("session-bound");
                var session = await context.Sessions.OpenAsync(
                    context.Keyspace,
                    OneMinute,
                    null,
                    cancellationToken
                );

                var first = await context.Leases.TryAcquireAsync(
                    resource,
                    LeaseTerms.ForSession(session.Id),
                    null,
                    cancellationToken
                );
                var firstToken = GrantOf(first).FencingToken.Value;

                var terms = LeaseTerms.ForTimeToLive(OneMinute);
                var blocked = await context.Leases.TryAcquireAsync(
                    resource,
                    terms,
                    null,
                    cancellationToken
                );
                Assert.Equal(LeaseAcquireStatus.Held, blocked.Status);

                await context.Sessions.CloseAsync(session.Id, null, cancellationToken);
                var grantCount = await context.GrantCountAsync(cancellationToken);

                Assert.Equal(1, grantCount);

                var second = await context.Leases.TryAcquireAsync(
                    resource,
                    terms,
                    null,
                    cancellationToken
                );
                Assert.True(GrantOf(second).FencingToken.Value > firstToken);
            },
            cancellationToken
        );
    }

    #endregion
}
