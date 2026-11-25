// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The state of a role: its leader, or none.
/// </summary>
public sealed class ElectionView
{
    /// <summary>
    /// Creates an election view.
    /// </summary>
    /// <param name="role">The role the view is of.</param>
    /// <param name="leader">The leader of the role, or null when it has none.</param>
    /// <exception cref="ArgumentNullException">The role is null.</exception>
    /// <exception cref="ArgumentException">The leader leads another role.</exception>
    public ElectionView(CoordinationKey role, Leader? leader)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (leader is not null && !role.Equals(leader.Role))
        {
            throw new ArgumentException("The leader leads another role.", nameof(leader));
        }

        Role = role;
        Leader = leader;
    }

    /// <summary>
    /// Gets the role the view is of.
    /// </summary>
    public CoordinationKey Role { get; }

    /// <summary>
    /// Gets the leader of the role, or null when the role has none.
    /// </summary>
    public Leader? Leader { get; }
}
