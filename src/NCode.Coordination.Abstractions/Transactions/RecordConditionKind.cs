// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The kind of a condition a transaction checks on a record.
/// </summary>
public enum RecordConditionKind
{
    /// <summary>
    /// The record does not exist.
    /// </summary>
    Absent,

    /// <summary>
    /// The record exists and has the given revision.
    /// </summary>
    RevisionEquals,

    /// <summary>
    /// The record exists and has the given value.
    /// </summary>
    ValueEquals,
}
