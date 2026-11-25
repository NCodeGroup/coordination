// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of deleting a record.
/// </summary>
public enum RecordDeleteStatus
{
    /// <summary>
    /// The record was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// The record's revision differed from the one expected.
    /// </summary>
    Conflict,

    /// <summary>
    /// The record does not exist.
    /// </summary>
    NotFound,
}
