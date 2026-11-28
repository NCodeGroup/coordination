// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlProviderProbeTests(PgServers servers)
{
    private const string RolePassword = "probe";

    private static string NewSchemaName() =>
        "probe_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static PostgreSqlProvider CreateProvider(
        string connectionString,
        PostgreSqlProfile profile,
        string schema,
        bool applyMigrations = true
    )
    {
        var source = PgSource.FromConnectionString(connectionString);
        var schemaName = PgSchemaName.Create(schema);
        var settings = PgSettings.Default with
        {
            Schema = schemaName,
            ApplyMigrations = applyMigrations,
        };

        return new PostgreSqlProvider(source, profile, settings);
    }

    private static async Task ProbeAsync(
        string connectionString,
        PostgreSqlProfile profile,
        string schema,
        CancellationToken cancellationToken,
        bool applyMigrations = true
    )
    {
        await using var provider = CreateProvider(
            connectionString,
            profile,
            schema,
            applyMigrations
        );

        await provider.ProbeAsync(cancellationToken);
    }

    private static async Task<CoordinationException> ProbeFailsAsync(
        string connectionString,
        PostgreSqlProfile profile,
        string schema,
        CancellationToken cancellationToken,
        bool applyMigrations = true
    )
    {
        await using var provider = CreateProvider(
            connectionString,
            profile,
            schema,
            applyMigrations
        );

        return await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );
    }

    private static async Task WithSchemaAsync(string adminConnectionString, Func<string, Task> test)
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

    private static async Task WithRoleAsync(
        string adminConnectionString,
        Func<string, string, Task> test,
        CancellationToken cancellationToken
    )
    {
        var role = "probe_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await PgServers.ExecuteAsAdminAsync(
            adminConnectionString,
            $"CREATE ROLE {role} LOGIN PASSWORD '{RolePassword}'",
            cancellationToken
        );

        try
        {
            var roleConnectionString = PgServers.AsRole(adminConnectionString, role, RolePassword);
            await test(role, roleConnectionString);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                adminConnectionString,
                $"DROP ROLE IF EXISTS {role}",
                CancellationToken.None
            );
        }
    }

    private static async Task<CoordinationException?> ProbeFailsOrNullAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var provider = CreateProvider(
            connectionString,
            PostgreSqlProfiles.Standalone,
            schema,
            applyMigrations: false
        );

        try
        {
            await provider.ProbeAsync(cancellationToken);
            return null;
        }
        catch (CoordinationException exception)
        {
            return exception;
        }
    }

    #region ProbeAsync Tests

    [PgServerFact]
    public async Task ProbeAsync_PostgreSql19AsCoordinator_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
            await ProbeAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            )
        );
    }

    [PgServerFact]
    public async Task ProbeAsync_PostgreSql15AsCoordinator_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin15, async schema =>
            await ProbeAsync(
                servers.PostgreSql15,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            )
        );
    }

    [PgServerFact]
    public async Task ProbeAsync_SynchronousProfileWithoutStandby_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            var exception = await ProbeFailsAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Synchronous,
                schema,
                cancellationToken
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
            Assert.Contains(
                "synchronous_standby_names",
                exception.Message,
                StringComparison.Ordinal
            );
        });
    }

    [PgServerFact]
    public async Task ProbeAsync_FsyncOff_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Unsafe19, async schema =>
        {
            var exception = await ProbeFailsAsync(
                servers.Unsafe19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
            Assert.Contains("fsync", exception.Message, StringComparison.Ordinal);
        });
    }

    [PgServerFact]
    public async Task ProbeAsync_RoleSeesSynchronousCommitOff_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRoleAsync(
            servers.Admin19,
            async (role, roleConnectionString) =>
            {
                await PgServers.ExecuteAsAdminAsync(
                    servers.Admin19,
                    $"ALTER ROLE {role} SET synchronous_commit = off",
                    cancellationToken
                );

                await WithSchemaAsync(servers.Admin19, async schema =>
                {
                    var exception = await ProbeFailsAsync(
                        roleConnectionString,
                        PostgreSqlProfiles.Standalone,
                        schema,
                        cancellationToken
                    );

                    Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
                    Assert.False(exception.OutcomeUnknown);
                    Assert.Contains(
                        "synchronous_commit",
                        exception.Message,
                        StringComparison.Ordinal
                    );
                });
            },
            cancellationToken
        );
    }

    [PgServerFact]
    public async Task ProbeAsync_NoTablesAndMigrationsOff_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            var exception = await ProbeFailsAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken,
                applyMigrations: false
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
            Assert.Contains("has no version", exception.Message, StringComparison.Ordinal);
        });
    }

    [PgServerFact]
    public async Task ProbeAsync_SchemaNewerButCompatible_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            await ProbeAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"INSERT INTO \"{schema}\".schema_version (version, compatible_from) VALUES ({PgMigrations.Latest + 1}, 1)",
                cancellationToken
            );

            await ProbeAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );
        });
    }

    [PgServerFact]
    public async Task ProbeAsync_SchemaNewerAndIncompatible_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSchemaAsync(servers.Admin19, async schema =>
        {
            await ProbeAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );
            var newer = PgMigrations.Latest + 1;
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"INSERT INTO \"{schema}\".schema_version (version, compatible_from) VALUES ({newer}, {newer})",
                cancellationToken
            );

            var exception = await ProbeFailsAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
            Assert.Contains("Upgrade the provider", exception.Message, StringComparison.Ordinal);
        });
    }

    [PgServerFact]
    public async Task ProbeAsync_RoleWithoutTablePrivileges_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await ProbeAsRoleWithGrantsAsync([], cancellationToken);

        var allTables = PgMigrations.Tables.Append("schema_version").Order(StringComparer.Ordinal);
        var tableList = string.Join(", ", allTables);
        var expectedText = "privileges on " + tableList + " in schema";

        Assert.NotNull(exception);
        Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains(expectedText, exception.Message, StringComparison.Ordinal);
    }

    [PgServerFact]
    public async Task ProbeAsync_RoleWithOnlySelect_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await ProbeAsRoleWithGrantsAsync(
            ["GRANT SELECT ON ALL TABLES IN SCHEMA {0} TO {1}"],
            cancellationToken
        );

        var orderedTables = PgMigrations.Tables.Order(StringComparer.Ordinal);
        var tableList = string.Join(", ", orderedTables);
        var expectedText = "privileges on " + tableList + " in schema";

        Assert.NotNull(exception);
        Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains(expectedText, exception.Message, StringComparison.Ordinal);
    }

    [PgServerFact]
    public async Task ProbeAsync_RoleWithAllPrivileges_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tableGrants = PgMigrations.Tables.Select(table =>
            "GRANT SELECT, INSERT, UPDATE, DELETE ON {0}." + table + " TO {1}"
        );
        string[] grants = ["GRANT SELECT ON {0}.schema_version TO {1}", .. tableGrants];

        var exception = await ProbeAsRoleWithGrantsAsync(grants, cancellationToken);

        Assert.Null(exception);
    }

    [PgServerFact]
    public async Task ProbeAsync_RoleWithoutCreate_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithRoleAsync(
            servers.Admin19,
            async (_, roleConnectionString) =>
                await WithSchemaAsync(servers.Admin19, async schema =>
                {
                    var exception = await ProbeFailsAsync(
                        roleConnectionString,
                        PostgreSqlProfiles.Standalone,
                        schema,
                        cancellationToken
                    );

                    Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
                    Assert.False(exception.OutcomeUnknown);
                }),
            cancellationToken
        );
    }

    #endregion

    private async Task<CoordinationException?> ProbeAsRoleWithGrantsAsync(
        string[] grants,
        CancellationToken cancellationToken
    )
    {
        CoordinationException? exception = null;
        await WithRoleAsync(
            servers.Admin19,
            async (role, roleConnectionString) =>
            {
                await WithSchemaAsync(servers.Admin19, async schema =>
                {
                    await ProbeAsync(
                        servers.Admin19,
                        PostgreSqlProfiles.Standalone,
                        schema,
                        cancellationToken
                    );
                    await PgServers.ExecuteAsAdminAsync(
                        servers.Admin19,
                        $"GRANT USAGE ON SCHEMA \"{schema}\" TO {role}",
                        cancellationToken
                    );
                    foreach (var grant in grants)
                    {
                        var statement = string.Format(
                            CultureInfo.InvariantCulture,
                            grant,
                            $"\"{schema}\"",
                            role
                        );
                        await PgServers.ExecuteAsAdminAsync(
                            servers.Admin19,
                            statement,
                            cancellationToken
                        );
                    }

                    exception = await ProbeFailsOrNullAsync(
                        roleConnectionString,
                        schema,
                        cancellationToken
                    );
                });
            },
            cancellationToken
        );

        return exception;
    }
}
