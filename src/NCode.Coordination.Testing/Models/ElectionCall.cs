// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on an election that a history records.
/// </summary>
/// <param name="Operation">The operation: <see cref="Lead"/>, <see cref="Renew"/>, <see cref="Resign"/> or <see cref="Read"/>.</param>
/// <param name="Value">The value the call offers followers, or null for any other operation than a lead.</param>
/// <param name="Owner">The owner value the call carries, or null for a lead or a read.</param>
/// <param name="Term">The term value the call carries, or 0 for a lead or a read.</param>
internal sealed record ElectionCall(string Operation, string? Value, string? Owner, long Term)
{
    /// <summary>
    /// The operation that tries to lead the role.
    /// </summary>
    public const string Lead = "lead";

    /// <summary>
    /// The operation that renews the leadership.
    /// </summary>
    public const string Renew = "renew";

    /// <summary>
    /// The operation that resigns the leadership.
    /// </summary>
    public const string Resign = "resign";

    /// <summary>
    /// The operation that reads the leader.
    /// </summary>
    public const string Read = "read";
}
