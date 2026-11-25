// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// The outcome of checking a whole history, with the first key that failed and the calls that show the failure.
/// </summary>
/// <param name="Verdict">What the check decided.</param>
/// <param name="Partition">The key whose history failed, or null when every key passed.</param>
/// <param name="Operations">The calls of the failing key that show the failure, empty when every key passed.</param>
internal sealed record LinearizabilityResult(
    LinearizabilityVerdict Verdict,
    string? Partition,
    ImmutableArray<HistoryOperation> Operations
)
{
    /// <summary>
    /// Gets the result of a history in which every key has a linearization.
    /// </summary>
    public static LinearizabilityResult Linearizable { get; } =
        new(LinearizabilityVerdict.Linearizable, null, []);
}
