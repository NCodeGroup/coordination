// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A barrier that holds each party until every party has arrived, built on records and record watches.
/// </summary>
/// <remarks>
/// A party counts once it is seen arriving, even when its record later ends. A key is used for one barrier once. The
/// keys under the barrier belong to it. Failures and cancellation throw as the record operations do.
/// </remarks>
public interface IBarrierService
{
    /// <summary>
    /// Records a party's arrival at a barrier, then waits until every party has arrived.
    /// </summary>
    /// <remarks>
    /// It returns true once every party has arrived, and false when the session in the lifetime ended before the
    /// arrival was recorded. A participant that arrives twice counts once.
    /// </remarks>
    /// <param name="barrier">The key of the barrier.</param>
    /// <param name="participant">The segment that names the arriving party.</param>
    /// <param name="terms">How many parties the barrier holds and how long an arrival lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when every party has arrived, and false when the session in the lifetime has ended.</returns>
    ValueTask<bool> ArriveAndWaitAsync(
        CoordinationKey barrier,
        KeySegment participant,
        BarrierTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
