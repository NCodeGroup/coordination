// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a semaphore that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Acquire"/>, <see cref="Renew"/> or <see cref="Release"/>.</param>
/// <param name="Slot">The slot the call carries, or 0 for an acquire.</param>
/// <param name="Owner">The owner value the call carries, or null for an acquire.</param>
/// <param name="Token">The fencing token value the call carries, or 0 for an acquire.</param>
internal sealed record SemaphoreCall(string Operation, int Slot, string? Owner, long Token)
{
    /// <summary>
    /// The operation that acquires a slot.
    /// </summary>
    public const string Acquire = "acquire";

    /// <summary>
    /// The operation that renews a slot.
    /// </summary>
    public const string Renew = "renew";

    /// <summary>
    /// The operation that releases a slot.
    /// </summary>
    public const string Release = "release";
}
