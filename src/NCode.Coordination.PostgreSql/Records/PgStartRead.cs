// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds the results of the first batch of a keyspace's change, as the batch's statements returned them.
/// </summary>
/// <param name="Revisions">The keyspace's revision, or no value when the keyspace's row does not exist.</param>
/// <param name="Clocks">The server's instant.</param>
/// <param name="Rows">The rows of the asked keys that exist.</param>
/// <param name="Sessions">The id and deadline of each asked session whose row exists.</param>
internal sealed record PgStartRead(
    ImmutableArray<long> Revisions,
    ImmutableArray<DateTime> Clocks,
    ImmutableArray<PgRecordRow> Rows,
    ImmutableArray<(string Id, DateTime Deadline)> Sessions
);
