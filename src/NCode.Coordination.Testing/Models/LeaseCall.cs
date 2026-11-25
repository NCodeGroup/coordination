// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on a lease that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Acquire"/>, <see cref="Renew"/> or <see cref="Release"/>.</param>
/// <param name="Owner">The owner value the call carries, or null for an acquire.</param>
/// <param name="Token">The fencing token value the call carries, or 0 for an acquire.</param>
/// <param name="TimeToLive">The time to live the call asks for, which a release ignores.</param>
internal sealed record LeaseCall(string Operation, string? Owner, long Token, TimeSpan TimeToLive)
{
    /// <summary>
    /// The operation that acquires the lease.
    /// </summary>
    public const string Acquire = "acquire";

    /// <summary>
    /// The operation that renews the lease.
    /// </summary>
    public const string Renew = "renew";

    /// <summary>
    /// The operation that releases the lease.
    /// </summary>
    public const string Release = "release";
}
