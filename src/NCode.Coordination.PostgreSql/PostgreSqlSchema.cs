// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Schema;

namespace NCode.Coordination;

/// <summary>
/// The schema of the PostgreSQL provider, whose migrations a host that applies schema changes itself runs in order
/// instead of letting the provider apply them.
/// </summary>
public static class PostgreSqlSchema
{
    /// <summary>
    /// The name of the schema that holds the provider's tables unless the options name another.
    /// </summary>
    public const string DefaultName = "coordination";

    /// <summary>
    /// Gets the scripts that create and migrate the schema, which a host that applies schema changes itself runs in
    /// the order returned, each once, in its own transaction.
    /// </summary>
    /// <param name="schema">The name of the schema: 1 to 63 lowercase letters, digits and underscores, starting with a
    /// letter or an underscore.</param>
    /// <returns>The migrations in order of their versions, each script with the schema's name applied.</returns>
    /// <exception cref="ArgumentNullException">The schema is null.</exception>
    /// <exception cref="ArgumentException">The schema is not a name the provider takes.</exception>
    public static IReadOnlyList<PostgreSqlMigration> GetMigrations(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var name = PgSchemaName.Create(schema);

        var migrations = new List<PostgreSqlMigration>(PgMigrations.All.Length);
        foreach (var migration in PgMigrations.All)
        {
            var statements = migration.Statements.Select(statement =>
                name.Apply(statement) + ";\n"
            );
            var script = string.Concat(statements);
            var published = new PostgreSqlMigration(
                migration.Version,
                migration.Name,
                migration.CompatibleFrom,
                script
            );

            migrations.Add(published);
        }

        return migrations;
    }
}
