// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of a transaction: which branch applied, or that a session a write binds to has ended, and the revision
/// after it.
/// </summary>
public sealed class TransactionResult
{
    /// <summary>
    /// Creates the result of a transaction.
    /// </summary>
    /// <param name="status">The outcome of the transaction.</param>
    /// <param name="revision">The revision of the keyspace after the transaction.</param>
    /// <exception cref="ArgumentNullException">The revision is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The status is not a defined value.</exception>
    public TransactionResult(TransactionStatus status, Revision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The status is not a transaction status."
            );
        }

        Status = status;
        Revision = revision;
    }

    /// <summary>
    /// Gets the outcome of the transaction.
    /// </summary>
    public TransactionStatus Status { get; }

    /// <summary>
    /// Gets a value indicating whether every condition held, so the writes on success applied.
    /// </summary>
    public bool Succeeded => Status == TransactionStatus.Succeeded;

    /// <summary>
    /// Gets the revision of the keyspace after the transaction, which is the revision it was evaluated at when nothing
    /// was written.
    /// </summary>
    public Revision Revision { get; }
}
