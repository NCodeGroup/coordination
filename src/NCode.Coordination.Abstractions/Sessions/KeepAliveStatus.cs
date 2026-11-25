// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of renewing a session.
/// </summary>
public enum KeepAliveStatus
{
    /// <summary>
    /// The session was renewed.
    /// </summary>
    Renewed,

    /// <summary>
    /// The session has ended.
    /// </summary>
    Ended,
}
