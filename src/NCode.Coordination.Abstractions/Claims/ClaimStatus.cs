// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of trying to claim a name in a time window.
/// </summary>
public enum ClaimStatus
{
    /// <summary>
    /// The caller is the first to claim the name in the window.
    /// </summary>
    Claimed,

    /// <summary>
    /// Another caller has claimed the name in the window.
    /// </summary>
    Taken,
}
