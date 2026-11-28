// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace NCode.Coordination.PostgreSql.Schema.Tests;

public sealed partial class PgMigrationsTests
{
    [GeneratedRegex(@"^CREATE TABLE \{schema\}\.(?<name>[a-z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedTable();

    #region All Property Tests

    [Fact]
    public void All_Versions_StartAtOneWithoutGaps()
    {
        var versions = PgMigrations.All.Select(migration => migration.Version).ToArray();

        var expectedVersions = Enumerable.Range(1, versions.Length);
        Assert.Equal(expectedVersions, versions);
        Assert.Equal(versions[^1], PgMigrations.Latest);
    }

    [Fact]
    public void All_EachScript_EndsByRecordingItsVersion()
    {
        foreach (var migration in PgMigrations.All)
        {
            var last = migration.Statements[^1];

            Assert.Contains(
                $"VALUES ({migration.Version}, {migration.CompatibleFrom})",
                last,
                StringComparison.Ordinal
            );
        }
    }

    #endregion

    #region Split Tests

    [Fact]
    public void Split_Script_SplitsAtLineEndSemicolons()
    {
        var statements = PgMigrations.Split(
            "SELECT 'a;b';\nSELECT 2;  \r\n\r\n  SELECT 3 ;\nSELECT 4"
        );

        Assert.Equal(["SELECT 'a;b'", "SELECT 2", "SELECT 3", "SELECT 4"], statements);
    }

    #endregion

    #region GetMigrations Tests

    [Fact]
    public void GetMigrations_Schema_AppliesIt()
    {
        var migrations = PostgreSqlSchema.GetMigrations("ncode");

        Assert.Equal(PgMigrations.All.Length, migrations.Count);
        foreach (var migration in migrations)
        {
            Assert.Contains("\"ncode\".schema_version", migration.Script, StringComparison.Ordinal);
            Assert.DoesNotContain("{schema}", migration.Script, StringComparison.Ordinal);
            Assert.EndsWith(";\n", migration.Script, StringComparison.Ordinal);
        }

        Assert.StartsWith(
            "CREATE SCHEMA IF NOT EXISTS \"ncode\";\n",
            migrations[0].Script,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void GetMigrations_InvalidSchema_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PostgreSqlSchema.GetMigrations("Not-A-Schema")
        );

        Assert.Equal("schema", exception.ParamName);
    }

    #endregion

    #region Tables Property Tests

    [Fact]
    public void Tables_EveryMigration_ListsEachTableItCreates()
    {
        var created = PgMigrations
            .All.SelectMany(migration => migration.Statements)
            .Select(statement => CreatedTable().Match(statement))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Where(name => name != "schema_version")
            .Order(StringComparer.Ordinal);

        var listed = PgMigrations.Tables.Order(StringComparer.Ordinal);

        Assert.Equal(listed, created);
    }

    #endregion
}
