// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;

namespace NCode.Coordination.PostgreSql.Transactions;

/// <summary>
/// Holds the rows that a transaction's first batch read, by what the transaction may do with each.
/// </summary>
/// <param name="Live">The live rows, by key.</param>
/// <param name="Replaceable">The rows that a write may replace without logging their removal, by key.</param>
/// <param name="Expired">The rows that have expired, whose removal the transaction logs.</param>
internal sealed record PgTransactionRows(
    Dictionary<CoordinationKey, PgRecordRow> Live,
    Dictionary<CoordinationKey, PgRecordRow> Replaceable,
    List<PgRecordRow> Expired
);
