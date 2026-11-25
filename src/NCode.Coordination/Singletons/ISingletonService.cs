// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Runs work on one node at a time, the leader of a role, built on elections.
/// </summary>
/// <remarks>
/// A candidate campaigns with its session. While it leads, the work runs with a token that is cancelled when the
/// leadership ends or the call is cancelled. A leadership that is lost is followed by a new campaign, and a former
/// leader's work can still be finishing when the next leader starts, so a resource checks the term, which is the
/// leadership's fencing token. Failures and cancellation throw as the election operations do.
/// </remarks>
public interface ISingletonService
{
    /// <summary>
    /// Campaigns for a role and runs the work while the candidate leads it.
    /// </summary>
    /// <remarks>
    /// It returns true after the work completes and the leadership is resigned, and false when the candidate's
    /// session has ended. When the leadership is lost while the work runs, the work's token is cancelled and the
    /// candidate campaigns again. Cancelling the call, or a failure of the work, resigns the leadership and throws.
    /// </remarks>
    /// <param name="role">The key of the role.</param>
    /// <param name="candidate">The candidate: its session and the value followers read.</param>
    /// <param name="work">The work to run as the leader, with the leadership and a token its end cancels.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the work completed, and false when the candidate's session has ended.</returns>
    ValueTask<bool> RunAsync(
        CoordinationKey role,
        SingletonCandidate candidate,
        Func<Leadership, CancellationToken, ValueTask> work,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
