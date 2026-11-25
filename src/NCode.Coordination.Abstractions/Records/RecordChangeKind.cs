// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The kind of a change a record watch reports.
/// </summary>
public enum RecordChangeKind
{
    /// <summary>
    /// A record was written.
    /// </summary>
    Written,

    /// <summary>
    /// A record was deleted, or it expired.
    /// </summary>
    Deleted,

    /// <summary>
    /// The subscriber may have missed changes: it rereads the records, then applies the changes after the revision.
    /// </summary>
    Resync,
}
