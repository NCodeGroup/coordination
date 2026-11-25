// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a transaction that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Read"/> or <see cref="Execute"/>.</param>
/// <param name="Key">The key a read reads, <see cref="FirstKey"/> or <see cref="SecondKey"/>, or null for an execute.</param>
/// <param name="Expected">The value the condition expects the first record to hold, or null when it expects it absent.</param>
/// <param name="Value">The value a transaction that succeeds writes to both records, or null for a read.</param>
internal sealed record TransactionCall(
    string Operation,
    string? Key,
    string? Expected,
    string? Value
)
{
    /// <summary>
    /// The operation that reads one of the two records.
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// The operation that executes a transaction on both records.
    /// </summary>
    public const string Execute = "execute";

    /// <summary>
    /// The key of the first record, which a transaction's condition checks.
    /// </summary>
    public const string FirstKey = "a";

    /// <summary>
    /// The key of the second record.
    /// </summary>
    public const string SecondKey = "b";
}
