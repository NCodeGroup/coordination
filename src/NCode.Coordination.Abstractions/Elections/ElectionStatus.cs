// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of trying to lead a role.
/// </summary>
public enum ElectionStatus
{
    /// <summary>
    /// The caller was elected leader.
    /// </summary>
    Elected,

    /// <summary>
    /// Another node leads the role.
    /// </summary>
    Led,

    /// <summary>
    /// The session the leadership was to be bound to has ended.
    /// </summary>
    SessionEnded,
}
