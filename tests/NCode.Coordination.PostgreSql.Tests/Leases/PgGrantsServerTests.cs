// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static TimeSpan OneMinuteSpan { get; } = TimeSpan.FromMinutes(1);

    private static TimeToLive OneMinute { get; } = new(OneMinuteSpan);

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

        public async ValueTask<long> LastFencingAsync(CancellationToken cancellationToken)
        {
            var lasts = await QueryAsync(
                "SELECT last FROM {schema}.fencing",
                reader => reader.GetInt64(0),
                cancellationToken
            );
            return Assert.Single(lasts);
        }

        public async ValueTask<int> GrantCountAsync(CancellationToken cancellationToken)
        {
            var counts = await QueryAsync(
                "SELECT count(*)::int FROM {schema}.grants",
                reader => reader.GetInt32(0),
                cancellationToken
            );
            return Assert.Single(counts);
        }
    }

    private async ValueTask WithServicesAsync(
        Func<Context, ValueTask> test,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "grants_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
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

            var grants = new PgGrants(table);
            var leases = new PgLeaseService(grants, tokens, polling);
            var sessions = new PgSessionService(table, tokens, polling);
            var sweeps = new PgSweeps(server, settings, table, NullLogger<PgSweeps>.Instance);
            var context = new Context(servers.Admin19, schemaName, leases, sessions, sweeps);

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
                var terms = LeaseTerms.ForTimeToLive(OneMinute);

                var first = await context.Leases.TryAcquireAsync(
                    resource,
                    terms,
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
                    terms,
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

                var sessionTerms = LeaseTerms.ForSession(session.Id);
                var terms = LeaseTerms.ForTimeToLive(OneMinute);

                var first = await context.Leases.TryAcquireAsync(
                    resource,
                    sessionTerms,
                    null,
                    cancellationToken
                );
                var firstToken = GrantOf(first).FencingToken.Value;

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
                var secondToken = GrantOf(second).FencingToken.Value;
                Assert.True(secondToken > firstToken);
            },
            cancellationToken
        );
    }

    #endregion
}
