// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of trying to record an occurrence.
/// </summary>
public enum OccurrenceStatus
{
    /// <summary>
    /// The key had not been recorded, and the call recorded it.
    /// </summary>
    Recorded,

    /// <summary>
    /// The key was already recorded and has not expired.
    /// </summary>
    Duplicate,
}
