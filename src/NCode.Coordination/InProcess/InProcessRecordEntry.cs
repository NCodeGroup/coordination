// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A record held by the in-process record table, with the sequence of the write that stored it.
/// </summary>
/// <param name="Record">The stored record.</param>
/// <param name="Sequence">
/// The sequence of the write that stored the record, unique per write and greater for a later write.
/// </param>
internal readonly record struct InProcessRecordEntry(StoredRecord Record, long Sequence);
