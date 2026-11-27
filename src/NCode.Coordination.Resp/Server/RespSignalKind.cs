// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// The kinds of signal that a channel reader receives.
/// </summary>
internal enum RespSignalKind
{
    /// <summary>
    /// A message that the channel carried.
    /// </summary>
    Message,

    /// <summary>
    /// The periodic check is due.
    /// </summary>
    Check,

    /// <summary>
    /// A connection of the client failed.
    /// </summary>
    Failed,

    /// <summary>
    /// A connection of the client was restored.
    /// </summary>
    Restored,
}
