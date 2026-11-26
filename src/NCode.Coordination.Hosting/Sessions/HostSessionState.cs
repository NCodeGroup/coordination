// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Where the host's coordination session stands.
/// </summary>
public enum HostSessionState
{
    /// <summary>
    /// The host has not started yet.
    /// </summary>
    NotStarted,

    /// <summary>
    /// The host holds no session and is opening one.
    /// </summary>
    Opening,

    /// <summary>
    /// The host holds a session, and the provider's last call for it succeeded.
    /// </summary>
    Active,

    /// <summary>
    /// The provider failed the host's last call, an open, a join or a renewal, whether or not the host still holds a
    /// session.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The host has stopped and holds no session.
    /// </summary>
    Stopped,
}
