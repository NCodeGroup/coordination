// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The kind of a notification a subscription delivers.
/// </summary>
public enum NotificationKind
{
    /// <summary>
    /// A message published on the topic.
    /// </summary>
    Message,

    /// <summary>
    /// The subscriber may have missed messages: it rereads the state the topic announces.
    /// </summary>
    Resync,
}
