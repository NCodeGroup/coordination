// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of writing a record.
/// </summary>
public enum RecordWriteStatus
{
    /// <summary>
    /// The record was written.
    /// </summary>
    Written,

    /// <summary>
    /// The record's state differed from what the write required.
    /// </summary>
    Conflict,

    /// <summary>
    /// The record to replace does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The session the record was to be bound to has ended.
    /// </summary>
    SessionEnded,
}
