// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of one election in its sequential specification.
/// </summary>
/// <param name="Owner">The owner value of the leader, or null when the role has no leader.</param>
/// <param name="Term">The term value of the leader, or 0 when it is unknown or the role has no leader.</param>
/// <param name="Floor">The highest term value known so far.</param>
/// <param name="Value">The value the leader offers followers, or null when the role has no leader.</param>
internal sealed record ElectionState(string? Owner, long Term, long Floor, string? Value);
