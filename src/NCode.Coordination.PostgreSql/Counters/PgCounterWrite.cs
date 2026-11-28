// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// Holds the count and deadline that a batch writes to one counter's row.
/// </summary>
/// <param name="Entry">The row to write.</param>
/// <param name="Count">The count to write.</param>
/// <param name="Deadline">The instant on the server's clock at which the counter expires.</param>
internal sealed record PgCounterWrite(PgCounterEntry Entry, long Count, DateTime Deadline);
