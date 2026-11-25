// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// How a recorded call ended.
/// </summary>
internal enum HistoryOutcome
{
    /// <summary>
    /// The call returned a result.
    /// </summary>
    Returned,

    /// <summary>
    /// The call failed and is known to have taken no effect.
    /// </summary>
    Failed,

    /// <summary>
    /// The call failed or never answered, and may have applied at any point after its call.
    /// </summary>
    Indeterminate,
}
