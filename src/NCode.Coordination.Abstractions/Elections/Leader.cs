// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The leader of a role: the role, the term and the value the leader published.
/// </summary>
public sealed class Leader
{
    /// <summary>
    /// Creates a leader.
    /// </summary>
    /// <param name="role">The role the leader leads.</param>
    /// <param name="term">The leader's term, which is its fencing token.</param>
    /// <param name="value">The value followers read, such as the leader's address. The type keeps its own copy.</param>
    /// <exception cref="ArgumentNullException">The role or the term is null.</exception>
    /// <exception cref="ArgumentException">The value is longer than the limit.</exception>
    public Leader(CoordinationKey role, FencingToken term, ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(term);

        var copy = ByteValue.Copy(value, CoordinationLimits.MaxValueLength, nameof(value));

        Role = role;
        Term = term;
        Value = copy;
    }

    /// <summary>
    /// Gets the role the leader leads.
    /// </summary>
    public CoordinationKey Role { get; }

    /// <summary>
    /// Gets the leader's term, which is its fencing token.
    /// </summary>
    public FencingToken Term { get; }

    /// <summary>
    /// Gets the value followers read, opaque bytes such as the leader's address.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }
}
