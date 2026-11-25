// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The kind of a write a transaction applies to a record.
/// </summary>
public enum RecordWriteKind
{
    /// <summary>
    /// The record is written with a value and a lifetime.
    /// </summary>
    Put,

    /// <summary>
    /// The record is deleted.
    /// </summary>
    Delete,
}
