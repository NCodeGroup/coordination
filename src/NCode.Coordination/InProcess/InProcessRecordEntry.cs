// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A record held by the in-process record table, with the sequence of the write that stored it, the session it is
/// bound to and the deadline that ends it.
/// </summary>
/// <param name="Record">The stored record.</param>
/// <param name="Sequence">
/// The sequence of the write that stored the record, unique per write and greater for a later write.
/// </param>
/// <param name="Session">The session the record is bound to, or null when it is not bound to a session.</param>
/// <param name="Deadline">The deadline that ends the record, or null when it has no time to live.</param>
internal readonly record struct InProcessRecordEntry(
    StoredRecord Record,
    long Sequence,
    SessionId? Session,
    InProcessDeadline? Deadline
);
