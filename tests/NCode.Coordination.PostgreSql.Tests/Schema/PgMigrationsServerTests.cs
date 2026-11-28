// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Schema.Tests;

public sealed class PgMigrationsServerTests(PgServers servers)
{
    private static string NewSchemaName() =>
        "mig_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static async ValueTask WithSchemaAsync(
        string adminConnectionString,
        Func<string, ValueTask> test
    )
    {
        var schema = NewSchemaName();
        try
        {
            await test(schema);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                adminConnectionString,
                $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",
                CancellationToken.None
            );
        }
    }

    private static async ValueTask<List<(int Version, int CompatibleFrom)>> ReadRowsAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT version, compatible_from FROM \"{schema}\".schema_version ORDER BY version",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<(int Version, int CompatibleFrom)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var version = reader.GetInt32(0);
            var compatibleFrom = reader.GetInt32(1);
            rows.Add((version, compatibleFrom));
        }

        return rows;
    }

    private static async ValueTask AssertEachVersionOnceAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        var rows = await ReadRowsAsync(connectionString, schema, cancellationToken);

        var expectedVersions = PgMigrations.All.Select(migration => migration.Version);
        var actualVersions = rows.Select(r => r.Version);

        Assert.Equal(expectedVersions, actualVersions);
    }

    private static async ValueTask ApplyAsAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var source = PgSource.FromConnectionString(connectionString);
        var server = new PgServer(source, PgSettings.Default);

        var schemaName = PgSchemaName.Create(schema);

        await PgMigrations.ApplyAsync(server, schemaName, cancellationToken);
    }

    private static async ValueTask<(int Version, int CompatibleFrom)> ReadVersionAsAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var source = PgSource.FromConnectionString(connectionString);
        var server = new PgServer(source, PgSettings.Default);

        var schemaName = PgSchemaName.Create(schema);

        return await PgMigrations.ReadVersionAsync(server, schemaName, cancellationToken);
    }

    #region ApplyAsync Tests

    [PgServerFact]
    public async Task ApplyAsync_NewSchema_ReachesTheLatestVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            await ApplyAsAsync(servers.PostgreSql19, schema, cancellationToken);

            var version = await ReadVersionAsAsync(servers.PostgreSql19, schema, cancellationToken);
            Assert.Equal(PgMigrations.Latest, version.Version);
            Assert.Equal(PgMigrations.All[^1].CompatibleFrom, version.CompatibleFrom);
        });
    }

    [PgServerFact]
    public async Task ApplyAsync_OnPostgreSql15_ReachesTheLatestVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin15, async schema =>
        {
            await ApplyAsAsync(servers.PostgreSql15, schema, cancellationToken);

            var version = await ReadVersionAsAsync(servers.PostgreSql15, schema, cancellationToken);
            Assert.Equal(PgMigrations.Latest, version.Version);
            Assert.Equal(PgMigrations.All[^1].CompatibleFrom, version.CompatibleFrom);
        });
    }

    [PgServerFact]
    public async Task ApplyAsync_Twice_RecordsEachVersionOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            await ApplyAsAsync(servers.PostgreSql19, schema, cancellationToken);
            await ApplyAsAsync(servers.PostgreSql19, schema, cancellationToken);

            await AssertEachVersionOnceAsync(servers.PostgreSql19, schema, cancellationToken);
        });
    }

    [PgServerFact]
    public async Task ApplyAsync_FromFourProvidersAtOnce_AppliesEachMigrationOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            var range = Enumerable.Range(0, 4);
            var applies = range.Select(_ =>
                ApplyAsAsync(servers.PostgreSql19, schema, cancellationToken).AsTask()
            );
            await Task.WhenAll(applies);

            await AssertEachVersionOnceAsync(servers.PostgreSql19, schema, cancellationToken);
        });
    }

    [PgServerFact]
    public async Task ApplyAsync_SchemaOwnedByARoleWithoutCreate_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var role = "owner_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        const string password = "owner";
        await PgServers.ExecuteAsAdminAsync(
            servers.Admin19,
            $"CREATE ROLE {role} LOGIN PASSWORD '{password}'",
            cancellationToken
        );

        try
        {
            await WithSchemaAsync(servers.Admin19, async schema =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"CREATE SCHEMA \"{schema}\" AUTHORIZATION {role}",
                    cancellationToken
                );
                var ownerConnectionString = PgServers.AsRole(servers.Admin19, role, password);

                await ApplyAsAsync(ownerConnectionString, schema, cancellationToken);

                await AssertEachVersionOnceAsync(ownerConnectionString, schema, cancellationToken);
            });
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"DROP ROLE IF EXISTS {role}",
                CancellationToken.None
            );
        }
    }

    #endregion

    #region GetMigrations Tests

    [PgServerFact]
    public async Task GetMigrations_RunByTheHost_ReachTheLatestVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            foreach (var migration in PostgreSqlSchema.GetMigrations(schema))
            {
                foreach (var statement in PgMigrations.Split(migration.Script))
                {
                    await PgServers.ExecuteAsAdminAsync(
                        servers.Admin19,
                        statement,
                        cancellationToken
                    );
                }
            }

            var version = await ReadVersionAsAsync(servers.Admin19, schema, cancellationToken);
            Assert.Equal(PgMigrations.Latest, version.Version);
            Assert.Equal(PgMigrations.All[^1].CompatibleFrom, version.CompatibleFrom);
        });
    }

    #endregion

    #region ReadVersionAsync Tests

    [PgServerFact]
    public async Task ReadVersionAsync_NoTables_ReturnsZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var schema = NewSchemaName();

        var version = await ReadVersionAsAsync(servers.PostgreSql19, schema, cancellationToken);

        Assert.Equal((0, 0), version);
    }

    #endregion
}
