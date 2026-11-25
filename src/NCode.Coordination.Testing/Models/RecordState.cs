// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of one record in its sequential specification.
/// </summary>
/// <param name="Value">The value of the record, or null when the record is absent.</param>
/// <param name="Revision">The revision value of the record, or 0 when the record is present and its revision is unknown.</param>
/// <param name="Floor">The highest revision value known so far.</param>
internal sealed record RecordState(string? Value, long Revision, long Floor);
