// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A leadership a provider granted: the leader, the owner token and the terms.
/// </summary>
/// <remarks>
/// The leader keeps the leadership: renewing and resigning it need its owner token.
/// </remarks>
public sealed class Leadership
{
    /// <summary>
    /// Creates a leadership.
    /// </summary>
    /// <param name="leader">The leader the leadership made.</param>
    /// <param name="owner">The token that identifies the leader.</param>
    /// <param name="terms">How long the leadership lasts.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Leadership(Leader leader, OwnerToken owner, LeaseTerms terms)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(terms);

        Leader = leader;
        Owner = owner;
        Terms = terms;
    }

    /// <summary>
    /// Gets the leader the leadership made.
    /// </summary>
    public Leader Leader { get; }

    /// <summary>
    /// Gets the token that identifies the leader and that renewing and resigning need.
    /// </summary>
    public OwnerToken Owner { get; }

    /// <summary>
    /// Gets how long the leadership lasts.
    /// </summary>
    public LeaseTerms Terms { get; }
}
