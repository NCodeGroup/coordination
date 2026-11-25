// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a sequence that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Next"/> or <see cref="Reserve"/>.</param>
/// <param name="Count">The count of numbers that a reserve takes, or 1 for a next.</param>
internal sealed record SequenceCall(string Operation, int Count)
{
    /// <summary>
    /// The operation that returns the next number.
    /// </summary>
    public const string Next = "next";

    /// <summary>
    /// The operation that reserves a block of consecutive numbers.
    /// </summary>
    public const string Reserve = "reserve";
}
