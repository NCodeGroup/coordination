// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a claim that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Claim"/>.</param>
internal sealed record ClaimCall(string Operation)
{
    /// <summary>
    /// The operation that claims the name once.
    /// </summary>
    public const string Claim = "claim";
}
