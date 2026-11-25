// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The handle of one deadline, which runs its action when the deadline comes due unless it was cancelled first.
/// </summary>
/// <param name="expire">The action that runs when the deadline comes due.</param>
internal class InProcessDeadline(Action expire)
{
    /// <summary>
    /// Gets the action that runs when the deadline comes due.
    /// </summary>
    public Action Expire { get; } = expire;

    /// <summary>
    /// Gets or sets a value indicating whether the deadline has fired or was cancelled.
    /// </summary>
    public bool IsDone { get; set; }
}
