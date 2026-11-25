// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// What a search decided about one key's history.
/// </summary>
internal enum LinearizabilityVerdict
{
    /// <summary>
    /// The history has a linearization.
    /// </summary>
    Linearizable,

    /// <summary>
    /// No order of the calls is allowed by the specification and keeps the order of non-overlapping calls.
    /// </summary>
    NotLinearizable,

    /// <summary>
    /// The search reached its limit of configurations before it decided.
    /// </summary>
    Undecided,
}
