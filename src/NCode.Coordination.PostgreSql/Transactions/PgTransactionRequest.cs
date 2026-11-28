// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Transactions;

/// <summary>
/// Holds what a transaction's server transaction needs besides the keyspace's id: the transaction, the keys that its
/// conditions and writes name, and the sessions that its writes bind records to.
/// </summary>
/// <param name="Transaction">The transaction to execute.</param>
/// <param name="Keys">The distinct keys that the conditions and the writes of both branches name.</param>
/// <param name="Sessions">The ids of the distinct sessions of the transaction's keyspace that its writes bind records to.</param>
internal sealed record PgTransactionRequest(
    RecordTransaction Transaction,
    IReadOnlyList<CoordinationKey> Keys,
    IReadOnlyCollection<string> Sessions
);
