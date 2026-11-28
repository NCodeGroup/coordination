// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds what a read of a resource's grants returned.
/// </summary>
/// <param name="Now">The server's instant.</param>
/// <param name="Rows">The grant rows that exist, expired or not.</param>
internal readonly record struct PgGrantRead(DateTime Now, ImmutableArray<PgGrantRow> Rows);
