// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of occurrences: a key recorded at most once until it expires.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IOccurrenceService
{
    /// <summary>
    /// Tries to record a key as having occurred.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.OccurrenceTryRecord"/>. The first caller for the key is told recorded, and
    /// every other is told duplicate until the key expires. The operation fails closed: when the provider cannot
    /// answer, it throws and never answers recorded, so a caller refuses what the occurrence guards.
    /// </remarks>
    /// <param name="key">The key to record.</param>
    /// <param name="timeToLive">How long the key stays recorded.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Recorded when the key was not recorded before, or duplicate.</returns>
    ValueTask<OccurrenceStatus> TryRecordAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
