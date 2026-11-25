// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a counter that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Increment"/>, <see cref="Read"/>, <see cref="Reset"/> or <see cref="Batch"/>.</param>
/// <param name="Amount">The amount that an increment or a batch adds, or 0 for the other operations.</param>
/// <param name="Limit">The limit that a batch checks the count against, or 0 for the other operations.</param>
internal sealed record CounterCall(string Operation, long Amount, long Limit)
{
    /// <summary>
    /// The operation that adds an amount and returns the count.
    /// </summary>
    public const string Increment = "increment";

    /// <summary>
    /// The operation that returns the count.
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// The operation that sets the count back to zero.
    /// </summary>
    public const string Reset = "reset";

    /// <summary>
    /// The operation that adds an amount to one key as a batch of one increment, with a limit.
    /// </summary>
    public const string Batch = "batch";
}
