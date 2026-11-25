// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of joining a membership group.
/// </summary>
public enum MembershipJoinStatus
{
    /// <summary>
    /// The member joined the group.
    /// </summary>
    Joined,

    /// <summary>
    /// Another live session holds the node id.
    /// </summary>
    NodeIdTaken,

    /// <summary>
    /// The member's session has ended.
    /// </summary>
    SessionEnded,
}
