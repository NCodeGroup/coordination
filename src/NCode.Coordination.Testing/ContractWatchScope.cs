// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing;

/// <summary>
/// The records that a watch covers: the prefix its listing reads and the filter its changes pass.
/// </summary>
/// <param name="Prefix">The prefix that a listing of the watched records reads.</param>
/// <param name="Matches">The filter that tells the keys the watch covers.</param>
internal sealed record ContractWatchScope(
    CoordinationKey Prefix,
    Func<CoordinationKey, bool> Matches
);
