// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Schema;

/// <summary>
/// One migration of the schema: the statements that take it from the previous version to this one.
/// </summary>
/// <param name="Version">The version the schema has once the migration is applied.</param>
/// <param name="Name">The name of the migration, as its file is named after its version.</param>
/// <param name="CompatibleFrom">The oldest version of the provider's code that may run on the migrated schema.</param>
/// <param name="Statements">The statements, in order, whose <c>{schema}</c> placeholders are not yet applied.</param>
internal sealed record PgMigration(
    int Version,
    string Name,
    int CompatibleFrom,
    ImmutableArray<string> Statements
);
