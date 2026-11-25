// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on an occurrence that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Record"/>.</param>
internal sealed record OccurrenceCall(string Operation)
{
    /// <summary>
    /// The operation that records the occurrence once.
    /// </summary>
    public const string Record = "record";
}
