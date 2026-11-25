// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The members of a group at one revision.
/// </summary>
public sealed class MembershipView
{
    /// <summary>
    /// Creates a membership view.
    /// </summary>
    /// <param name="group">The group the view is of.</param>
    /// <param name="revision">The revision the view is at.</param>
    /// <param name="members">The members, in the provider's order.</param>
    /// <exception cref="ArgumentNullException">The group or the revision is null.</exception>
    /// <exception cref="ArgumentException">A member is null.</exception>
    public MembershipView(
        CoordinationKey group,
        Revision revision,
        params IEnumerable<Member> members
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(revision);

        Group = group;
        Revision = revision;
        Members = ElementList.Copy(members, nameof(members));
    }

    /// <summary>
    /// Gets the group the view is of.
    /// </summary>
    public CoordinationKey Group { get; }

    /// <summary>
    /// Gets the revision the view is at.
    /// </summary>
    public Revision Revision { get; }

    /// <summary>
    /// Gets the members, in the provider's order.
    /// </summary>
    public ImmutableArray<Member> Members { get; }
}
