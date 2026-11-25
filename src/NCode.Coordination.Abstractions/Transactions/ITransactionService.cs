// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of transactions: conditions on several records, then one set of writes when they hold and another
/// when they do not, applied together.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ITransactionService
{
    /// <summary>
    /// Evaluates the conditions of a transaction and applies the writes of the branch that follows.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.TransactionExecute"/>. It evaluates the conditions and writes in one atomic
    /// step. A provider that keeps only <see cref="Guarantees.AtomicWithinPartition"/> throws
    /// <see cref="CoordinationException"/> with <see cref="CoordinationErrorCodes.Unsupported"/> for keys in more than
    /// one partition, where two keys share a partition when they have the same
    /// <see cref="CoordinationKey.PartitionDepth"/> above 0 and equal leading segments to that depth, or are equal.
    /// </remarks>
    /// <param name="transaction">The conditions and the writes of each branch.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Whether the conditions held, and the revision after the transaction.</returns>
    ValueTask<TransactionResult> ExecuteAsync(
        RecordTransaction transaction,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
