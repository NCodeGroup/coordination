// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of claims: at most one holder of a name in each time window.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IClaimService
{
    /// <summary>
    /// Tries to claim a name in a time window.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ClaimTryClaim"/>. The first caller for the name and window is told
    /// claimed, and every other is told taken. The claim cannot be released and expires after the time to live, which
    /// the caller computes with <see cref="TimeWindow.TryCreateTimeToLive"/>, so it outlives the window. The provider
    /// stores the claim under the name followed by the window's <see cref="TimeWindow.Segment"/>. When the provider
    /// cannot answer, the operation throws rather than answering claimed.
    /// </remarks>
    /// <param name="name">The name to claim.</param>
    /// <param name="window">The time window the claim is for.</param>
    /// <param name="timeToLive">How long the claim lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Claimed when the caller is the first, or taken.</returns>
    ValueTask<ClaimStatus> TryClaimAsync(
        CoordinationKey name,
        TimeWindow window,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
