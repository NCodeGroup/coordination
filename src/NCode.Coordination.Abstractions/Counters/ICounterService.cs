// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of counters: counts under keys, each spanning one window.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ICounterService
{
    /// <summary>
    /// Adds an amount to a counter.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.CounterIncrement"/>. The amount is at least 1, else the operation throws
    /// <see cref="ArgumentOutOfRangeException"/>. The time to live applies when the increment creates the counter, so
    /// a window's counter ends with its window.
    /// </remarks>
    /// <param name="key">The key of the counter.</param>
    /// <param name="amount">The amount to add, at least 1.</param>
    /// <param name="timeToLive">How long the counter lasts when this increment creates it.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The count after the increment.</returns>
    ValueTask<long> IncrementAsync(
        CoordinationKey key,
        long amount,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the count of a counter.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.CounterRead"/>.
    /// </remarks>
    /// <param name="key">The key of the counter.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The count, or 0 when the counter is absent.</returns>
    ValueTask<long> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes a counter.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.CounterReset"/>.
    /// </remarks>
    /// <param name="key">The key of the counter.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the counter is deleted.</returns>
    ValueTask ResetAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Applies several increments in one atomic step, stopping after the first that exceeds its limit.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.CounterIncrementBatch"/>. A batch has 1 to
    /// <see cref="CoordinationLimits.MaxBatchIncrements"/> increments, all in one keyspace, else the operation throws
    /// <see cref="ArgumentException"/>. It applies them in order in one atomic step and stops after the first whose
    /// count then exceeds its limit, which stays applied with the earlier ones, while later ones are not applied.
    /// Across partitions it behaves as <see cref="ITransactionService"/> describes for a transaction.
    /// </remarks>
    /// <param name="increments">The increments, applied in order.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The count after each applied increment, and the index of the increment that exceeded its limit.</returns>
    ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
