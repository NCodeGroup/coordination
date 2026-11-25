// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of sequences: unique numbers that increase across every caller and never reset.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ISequenceService
{
    /// <summary>
    /// Takes the next number of a sequence.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SequenceNext"/>. Numbers start at 1.
    /// </remarks>
    /// <param name="key">The key of the sequence.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The number taken.</returns>
    ValueTask<long> NextAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reserves a block of consecutive numbers of a sequence.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SequenceReserve"/>. The count is from 1 to
    /// <see cref="CoordinationLimits.MaxSequenceBlock"/>, else the operation throws
    /// <see cref="ArgumentOutOfRangeException"/>. A reserved number the caller does not use is never handed out again.
    /// </remarks>
    /// <param name="key">The key of the sequence.</param>
    /// <param name="count">The count of consecutive numbers to reserve.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The reserved block.</returns>
    ValueTask<SequenceBlock> ReserveAsync(
        CoordinationKey key,
        int count,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
