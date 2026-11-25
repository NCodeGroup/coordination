// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of a transaction: which branch applied, and the revision after it.
/// </summary>
public sealed class TransactionResult
{
    /// <summary>
    /// Creates the result of a transaction.
    /// </summary>
    /// <param name="succeeded">Whether every condition held.</param>
    /// <param name="revision">The revision of the keyspace after the transaction.</param>
    /// <exception cref="ArgumentNullException">The revision is null.</exception>
    public TransactionResult(bool succeeded, Revision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        Succeeded = succeeded;
        Revision = revision;
    }

    /// <summary>
    /// Gets a value indicating whether every condition held, so the writes on success applied; otherwise the writes on
    /// failure applied.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the revision of the keyspace after the transaction.
    /// </summary>
    public Revision Revision { get; }
}
