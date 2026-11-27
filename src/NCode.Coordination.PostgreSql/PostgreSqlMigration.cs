// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// One migration of the PostgreSQL provider's schema, as a script that a host which applies schema changes itself
/// runs, in the order of <see cref="Version"/>.
/// </summary>
public sealed record PostgreSqlMigration
{
    /// <summary>
    /// Creates a migration.
    /// </summary>
    /// <param name="version">The version the schema has once the script has run.</param>
    /// <param name="name">The name of the migration.</param>
    /// <param name="compatibleFrom">The oldest version of the provider's code that may run on the migrated schema.</param>
    /// <param name="script">The SQL script, one statement per line-ending semicolon.</param>
    internal PostgreSqlMigration(int version, string name, int compatibleFrom, string script)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(script);

        Version = version;
        Name = name;
        CompatibleFrom = compatibleFrom;
        Script = script;
    }

    /// <summary>
    /// Gets the version the schema has once the script has run.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the name of the migration.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the oldest version of the provider's code that may run on the migrated schema.
    /// </summary>
    public int CompatibleFrom { get; }

    /// <summary>
    /// Gets the SQL script, whose statements each end with a semicolon and a line break and which records its own
    /// version in the schema's <c>schema_version</c> table.
    /// </summary>
    public string Script { get; }
}
