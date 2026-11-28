// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// Holds the results of the first batch of a change to a resource's grants, as the batch's statements returned them.
/// </summary>
/// <param name="Revisions">The keyspace's revision, or no value when the keyspace's row does not exist.</param>
/// <param name="Clocks">The server's instant.</param>
/// <param name="Rows">The resource's grant rows, expired or not, by slot.</param>
/// <param name="Lasts">The last fencing token given to the resource, or no value when it has been given none.</param>
/// <param name="Deadlines">The deadline of the terms' session, or no value when there is none or its row is gone.</param>
internal sealed record PgGrantStartRead(
    ImmutableArray<long> Revisions,
    ImmutableArray<DateTime> Clocks,
    ImmutableArray<PgGrantRow> Rows,
    ImmutableArray<long> Lasts,
    ImmutableArray<DateTime> Deadlines
);
